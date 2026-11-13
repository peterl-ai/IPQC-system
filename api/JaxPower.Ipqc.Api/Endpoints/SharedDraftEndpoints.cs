using System.Text.Json;
using System.Threading.RateLimiting;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Http.Features;

namespace JaxPower.Ipqc.Api.Endpoints;

public sealed record SharedDraftMutation;
public sealed class SharedDraftLimits : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter=PartitionedRateLimiter.Create<string,string>(key=>
        RateLimitPartition.GetFixedWindowLimiter(key,_=>new FixedWindowRateLimiterOptions { PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
    public RateLimitLease Acquire(string key)=>limiter.AttemptAcquire(key);
    public void Dispose()=>limiter.Dispose();
}
public static class SharedDraftEndpoints
{
    public static void MapSharedDrafts(this WebApplication app)
    {
        var g=app.MapGroup("/api/line-patrol/tasks/{taskId:guid}");
        PersonalAccess.RequireCurrent(g.MapGet("/shared-draft",async(Guid taskId,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.ReadAsync(taskId,h.User,ct))),true);
        PersonalAccess.RequireCurrent(g.MapPost("/operation-batches",async(Guid taskId,DraftBatchInput input,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.AcceptAsync(taskId,input,h.User,ct))),true)
            .RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new SharedDraftMutation());
        PersonalAccess.RequireCurrent(g.MapPost("/item-removal-batches",async(Guid taskId,ItemRemovalBatchInput input,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.RemoveItemAsync(taskId,input,h.User,ct))),true)
            .RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new SharedDraftMutation());
        PersonalAccess.RequireCurrent(g.MapPost("/attachment-reference-batches",async(Guid taskId,DraftAttachmentBatchInput input,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.AcceptAttachmentsAsync(taskId,input,h.User,ct))),true)
            .RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new SharedDraftMutation(),new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapPost("/item-disposition-batches",async(Guid taskId,UnableToMeasureBatchInput input,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.AcceptUnableAsync(taskId,input,h.User,ct))),true)
            .RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new SharedDraftMutation(),new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/shared-draft/item-dispositions",async(Guid taskId,long? beforeDraftVersion,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.ReadDispositionsAsync(taskId,h.User,ct,beforeDraftVersion))),true).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/shared-draft/evidence-assessment",async(Guid taskId,int? page,int? pageSize,long? expectedDraftVersion,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.AssessEvidenceAsync(taskId,h.User,ct,page??1,pageSize??20,expectedDraftVersion))),true).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapPost("/sample-evidence-batches",async(Guid taskId,SampleEvidenceBatchInput input,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.AcceptSampleEvidenceAsync(taskId,input,h.User,ct))),true).RequireAuthorization(IpqcPermissions.LinePatrolContributeDraft).WithMetadata(new SharedDraftMutation(),new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/shared-draft/evidence-bindings",async(Guid taskId,int? page,int? pageSize,long? beforeDraftVersion,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.ReadSampleEvidenceAsync(taskId,h.User,ct,page??1,pageSize??50,beforeDraftVersion))),true).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/shared-draft/attachments",async(Guid taskId,long? beforeDraftVersion,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.ReadAttachmentsAsync(taskId,h.User,ct,beforeDraftVersion))),true).WithMetadata(new PhotoRequest(false));
        PersonalAccess.RequireCurrent(g.MapGet("/operation-batches/{batchId:guid}/receipt",async(Guid taskId,Guid batchId,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.BatchReceiptAsync(taskId,batchId,h.User,ct))),true);
        PersonalAccess.RequireCurrent(g.MapGet("/operations/{operationId:guid}/receipt",async(Guid taskId,Guid operationId,SharedDraftService s,HttpContext h,CancellationToken ct)=>
            Results.Ok(await s.OperationReceiptAsync(taskId,operationId,h.User,ct))),true);
    }
    public static async Task Limits(HttpContext h,RequestDelegate next)
    {
        if(h.GetEndpoint()?.Metadata.GetMetadata<SharedDraftMutation>() is null) { await next(h);return; }
        using var lease=h.RequestServices.GetRequiredService<SharedDraftLimits>().Acquire(h.User.FindFirst("sub")!.Value);
        if(!lease.IsAcquired) {h.Response.StatusCode=429;return;}
        const int maximum=131072;
        if(h.Features.Get<IHttpMaxRequestBodySizeFeature>() is {IsReadOnly:false} feature) feature.MaxRequestBodySize=maximum;
        if(h.Request.ContentLength>maximum) {h.Response.StatusCode=413;return;}
        var original=h.Request.Body;using var bytes=new MemoryStream();var chunk=new byte[4096];int read;
        while((read=await original.ReadAsync(chunk,h.RequestAborted))!=0) {
            if(bytes.Length+read>maximum) {h.Response.StatusCode=413;return;} bytes.Write(chunk,0,read);
        }
        try {
            using var json=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions {MaxDepth=32});
            void Unique(JsonElement e) {
                if(e.ValueKind==JsonValueKind.Object) {
                    var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var p in e.EnumerateObject()) {if(!keys.Add(p.Name)) throw new JsonException();Unique(p.Value);}
                } else if(e.ValueKind==JsonValueKind.Array) foreach(var child in e.EnumerateArray()) Unique(child);
            }
            Unique(json.RootElement);
        } catch(JsonException) {throw new PersonalAccessException("InvalidDraftJson",400);}
        bytes.Position=0;h.Request.Body=bytes;
        try {await next(h);} finally {h.Request.Body=original;}
    }
}
