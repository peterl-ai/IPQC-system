using System.Threading.RateLimiting;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Http.Features;

namespace JaxPower.Ipqc.Api.Endpoints;

public sealed record PhotoRequest(bool Bytes);
public sealed class PhotoLimits : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter=PartitionedRateLimiter.Create<string,string>(key=>RateLimitPartition.GetFixedWindowLimiter(key,
        _=>new FixedWindowRateLimiterOptions {PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    public RateLimitLease Acquire(string key)=>limiter.AttemptAcquire(key);
    // Bound concurrent 5 MiB inputs and decoding/read buffers process-wide, independently of user IDs.
    internal SemaphoreSlim Slots {get;}=new(4);
    public void Dispose() {limiter.Dispose();Slots.Dispose();}
}
public static class InspectionAttachmentEndpoints
{
    public static void MapInspectionAttachments(this WebApplication app)
    {
        var g=app.MapGroup("/api/line-patrol/tasks/{taskId:guid}/inspection-attachments");
        PersonalAccess.RequireCurrent(g.MapPost("/",async(Guid taskId,AttachmentInput input,InspectionAttachmentService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.Reserve(taskId,input,h.User,ct))),true).RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new PhotoRequest(false),new SharedDraftMutation());
        PersonalAccess.RequireCurrent(g.MapPut("/{id:guid}/content",async(Guid taskId,Guid id,InspectionAttachmentService s,HttpContext h,CancellationToken ct)=> {
            if(h.Request.ContentType!="application/octet-stream") throw new PersonalAccessException("PhotoBinaryBodyRequired",415);
            return Results.Ok(await s.Upload(taskId,id,Proof(h),h.Request.Body,h.User,ct));
        }),true).RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new PhotoRequest(true));
        PersonalAccess.RequireCurrent(g.MapPost("/{id:guid}/recover",async(Guid taskId,Guid id,InspectionAttachmentService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.Recover(taskId,id,Proof(h),h.User,ct))),true).RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/{id:guid}",async(Guid taskId,Guid id,InspectionAttachmentService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.Read(taskId,id,false,h.User,ct))),true).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/by-client/{clientId:guid}",async(Guid taskId,Guid clientId,InspectionAttachmentService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.Read(taskId,clientId,true,h.User,ct))),true).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/{id:guid}/content",async(Guid taskId,Guid id,InspectionAttachmentService s,HttpContext h,CancellationToken ct)=> {
            var content=await s.Content(taskId,id,h.User,ct);h.Response.Headers.XContentTypeOptions="nosniff";
            return Results.File(content.Bytes,content.ContentType,$"inspection-{id:N}"+(content.ContentType=="image/png"?".png":".jpg"),enableRangeProcessing:false);
        }),true).WithMetadata(new PhotoRequest(false));
    }
    private static string Proof(HttpContext h) => h.Request.Headers["X-WorkContext-Proof"] is {Count:1} value && value[0] is {Length:43} proof?proof:"";
    public static async Task Limits(HttpContext h,RequestDelegate next)
    {
        var metadata=h.GetEndpoint()?.Metadata.GetMetadata<PhotoRequest>();if(metadata is null) {await next(h);return;}
        var limits=h.RequestServices.GetRequiredService<PhotoLimits>();
        using var lease=limits.Acquire(h.User.FindFirst("sub")!.Value);
        if(!lease.IsAcquired || !await limits.Slots.WaitAsync(0,h.RequestAborted)) {h.Response.StatusCode=429;return;}
        var aborted=h.RequestAborted;using var deadline=CancellationTokenSource.CreateLinkedTokenSource(aborted);deadline.CancelAfter(TimeSpan.FromMinutes(2));h.RequestAborted=deadline.Token;
        try {
            var maximum=metadata.Bytes?ProtectedPhotoStore.MaximumBytes:131072;
            if(h.Features.Get<IHttpMaxRequestBodySizeFeature>() is {IsReadOnly:false} feature) feature.MaxRequestBodySize=maximum;
            if(h.Request.ContentLength>maximum) {h.Response.StatusCode=413;return;}
            try {await next(h);} catch(PhotoStorageException ex) when(!h.Response.HasStarted) {
                h.Response.StatusCode=ex.Message.Contains("Unavailable") || ex.Message.Contains("Failed")?503:409;
                await h.Response.WriteAsJsonAsync(new {code=ex.Message},h.RequestAborted);
            } catch(OperationCanceledException) when(deadline.IsCancellationRequested && !aborted.IsCancellationRequested && !h.Response.HasStarted) {
                h.Response.StatusCode=408;await h.Response.WriteAsJsonAsync(new {code="PhotoRequestTimedOut"},aborted);
            }
        } finally {h.RequestAborted=aborted;limits.Slots.Release();}
    }
}
