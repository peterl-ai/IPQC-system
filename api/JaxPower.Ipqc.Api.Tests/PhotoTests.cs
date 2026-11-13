using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoTests
{
    [Theory][InlineData(false)][InlineData(true)] public async Task Real_bytes_and_immutable_replay(bool jpeg) {await using var p=await PhotoFixture.Create();await Lifecycle(p,jpeg);}
    internal static async Task Lifecycle(PhotoFixture p,bool jpeg=false)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image(jpeg);var input=PhotoFixture.Input(s,bytes,jpeg?"image/jpeg":"image/png");
        var reserved=await PhotoFixture.Reserve(s,input);Assert.False(reserved.IsReady);Assert.Null(reserved.ReadyAtUtc);Assert.Null(reserved.UploadedByUserId);
        var ready=await PhotoFixture.Ready(s,reserved.AttachmentId,bytes);Assert.True(ready.IsReady);Assert.Equal(s.A.User.Id,ready.ContributorUserId);Assert.Equal(s.B.User.Id,ready.UploadedByUserId);
        Assert.Equal(s.B.PersonalSessionId,ready.UploadedPersonalSessionId);Assert.Equal(input.Sha256,ready.Sha256);Assert.Equal(bytes.Length,ready.ByteCount);Assert.Equal(3,ready.Width);
        Assert.True(ready.CreatedAtUtc<=ready.StoredAtUtc && ready.StoredAtUtc<=ready.ReadyAtUtc);
        var read=await s.Client(s.A).GetAsync(PhotoFixture.Root(s)+$"/{ready.AttachmentId}/content");Assert.Equal(bytes,await read.Content.ReadAsByteArrayAsync());Assert.Equal("no-store",read.Headers.CacheControl!.ToString());Assert.Equal("nosniff",Assert.Single(read.Headers.GetValues("X-Content-Type-Options")));
        await SharedDraftFixture.Revoke(s);
        Assert.Equal(ready,await PhotoFixture.Reserve(s,input with {ProofSecret=""}));
        var replay=await PhotoFixture.Upload(s,ready.AttachmentId,bytes,s.B,"");Assert.Equal(ready,await replay.Content.ReadFromJsonAsync<AttachmentOutput>());
        var different=await PhotoFixture.Upload(s,ready.AttachmentId,PhotoFixture.Image(!jpeg));Assert.Equal(HttpStatusCode.Conflict,different.StatusCode);
        Assert.Equal(ready,await (await PhotoFixture.Recover(s,ready.AttachmentId,"")).Content.ReadFromJsonAsync<AttachmentOutput>());
        Assert.Equal(1,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));await s.AssertCounts(0,0,0,0);
        Assert.DoesNotContain("storageKey",await (await s.Client().GetAsync(PhotoFixture.Root(s)+$"/{ready.AttachmentId}")).Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
        using var listing=System.Text.Json.JsonDocument.Parse(await s.Client().GetStringAsync(s.Root));
        Assert.False(listing.RootElement.GetProperty("canExecute").GetBoolean());
    }
    [Theory][InlineData("bytes")][InlineData("source")][InlineData("item")][InlineData("attempt")][InlineData("task")]
    public async Task Same_client_identity_conflicts(string kind) {await using var p=await PhotoFixture.Create();await Conflict(p,kind);}
    internal static async Task Conflict(PhotoFixture p,string kind)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var i=PhotoFixture.Input(s,bytes);await PhotoFixture.Reserve(s,i);
        var changed=kind switch {"bytes"=>i with {Sha256=new string('A',64)},"source"=>i with {SourceWorkContextId=Guid.NewGuid()},"item"=>i with {TaskItemId=Guid.NewGuid()},"attempt"=>i with {InspectionAttemptId=Guid.NewGuid()},_=>i};
        var task=kind=="task"?await p.F.Task():s.TaskId;
        var response=await s.Client().PostAsJsonAsync($"/api/line-patrol/tasks/{task}/inspection-attachments",changed);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.Equal(1,await p.F.Read(db=>db.InspectionAttachments.CountAsync()));Assert.Empty(Directory.GetFiles(p.DirectoryPath));
    }
    [Theory][InlineData("hash")][InlineData("size")][InlineData("mime")][InlineData("corrupt")][InlineData("truncated")][InlineData("oversize")][InlineData("bomb")]
    public async Task Invalid_bytes_never_ready(string kind) {await using var p=await PhotoFixture.Create();await Invalid(p,kind);}
    internal static async Task Invalid(PhotoFixture p,string kind)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=kind=="oversize"?new byte[ProtectedPhotoStore.MaximumBytes+1]:kind=="bomb"?PhotoFixture.Image(false,6001,1):PhotoFixture.Image();
        if(kind=="corrupt") bytes[bytes.Length/2]^=1;
        if(kind=="truncated") bytes=bytes[..^10];
        var i=PhotoFixture.Input(s,bytes);if(kind=="hash") i=i with{Sha256=new string('0',64)};if(kind=="size") i=i with{ByteCount=bytes.Length-1};if(kind=="mime") i=i with{ContentType="image/jpeg"};
        if(kind=="oversize") {Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsJsonAsync(PhotoFixture.Root(s),i)).StatusCode);return;}
        var a=await PhotoFixture.Reserve(s,i);Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);
        Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));var status=await s.Client().GetFromJsonAsync<AttachmentOutput>(PhotoFixture.Root(s)+$"/{a.AttachmentId}");Assert.False(status!.IsReady);
    }
    [Fact] public async Task Authority_precedes_byte_reads() {await using var p=await PhotoFixture.Create();await Authority(p);}
    internal static async Task Authority(PhotoFixture p)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var i=PhotoFixture.Input(s,bytes);var a=await PhotoFixture.Reserve(s,i);
        var anonymous=await p.F.Anonymous.GetAsync(PhotoFixture.Root(s)+$"/{a.AttachmentId}/content");Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        var admin=await p.F.Login("admin");Assert.Equal(HttpStatusCode.Forbidden,(await PhotoFixture.Upload(s,a.AttachmentId,bytes,admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Upload(s,a.AttachmentId,bytes,proof:"invalid")).StatusCode);Assert.Empty(Directory.GetFiles(p.DirectoryPath));
        var other=await p.F.Task();Assert.Equal(HttpStatusCode.NotFound,(await s.Client().GetAsync($"/api/line-patrol/tasks/{other}/inspection-attachments/{a.AttachmentId}")).StatusCode);
        await SharedDraftFixture.Revoke(s);Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);Assert.Empty(Directory.GetFiles(p.DirectoryPath));
        Assert.Equal(0,await p.F.Read(db=>db.AttachmentUploads.CountAsync()));
    }
    [Theory][InlineData("logout")][InlineData("expiry")][InlineData("self-password")]
    public async Task Normal_A_lifecycle_does_not_revoke(string action) {await using var p=await PhotoFixture.Create();await NormalA(p,action);}
    internal static async Task NormalA(PhotoFixture p,string action)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));
        if(action=="logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout",new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        else if(action=="expiry") p.F.Clock.Now=p.F.Clock.Now.AddDays(15);
        else (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password",new ChangePasswordInput(TestAccounts.Password,"SyntheticChanged123A"))).EnsureSuccessStatusCode();
        s=s with{B=await p.F.Login("dev-ipqa-2")};var ready=await PhotoFixture.Ready(s,a.AttachmentId,bytes);Assert.Equal(s.A.User.Id,ready.ContributorUserId);
        Assert.Null(await p.F.Read(db=>db.WorkContexts.Where(x=>x.Id==s.Source.Context.Id).Select(x=>x.SecurityRevokedAtUtc).SingleAsync()));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Physical_loss_fails_closed(bool corrupt) {await using var p=await PhotoFixture.Create();await Loss(p,corrupt);}
    internal static async Task Loss(PhotoFixture p,bool corrupt)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));var ready=await PhotoFixture.Ready(s,a.AttachmentId,bytes);
        var path=await p.FinalPath(a.AttachmentId);if(corrupt) await File.WriteAllBytesAsync(path,[1,2,3]);else File.Delete(path);
        var status=await s.Client().GetFromJsonAsync<AttachmentOutput>(PhotoFixture.Root(s)+$"/{a.AttachmentId}");Assert.False(status!.IsReady);Assert.Equal(corrupt?"Corrupt":"MissingUnexpected",status.Availability);Assert.Equal(ready.ReadyAtUtc,status.ReadyAtUtc);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().GetAsync(PhotoFixture.Root(s)+$"/{a.AttachmentId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Recover(s,a.AttachmentId)).StatusCode);
        Assert.Equal(1,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
    }
    [Fact] public async Task Bounded_body_and_rate() {
        await using var p=await PhotoFixture.Create();var s=await SharedDraftFixture.Setup(p.F);var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,PhotoFixture.Image()));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await PhotoFixture.Upload(s,a.AttachmentId,new byte[ProtectedPhotoStore.MaximumBytes+1])).StatusCode);
        HttpResponseMessage? r=null;for(var n=0;n<22;n++) r=await s.Client().GetAsync(PhotoFixture.Root(s)+$"/{a.AttachmentId}");Assert.Equal(HttpStatusCode.TooManyRequests,r!.StatusCode);Assert.Empty(Directory.GetFiles(p.DirectoryPath));
    }
    [Theory][InlineData("missing")][InlineData("app")][InlineData("relative")]
    public async Task Unsafe_configuration_is_closed(string kind) {
        await using var p=await PhotoFixture.Create();var s=await SharedDraftFixture.Setup(p.F);var config=p.F.Factory.Services.GetRequiredService<IConfiguration>();
        config["InspectionPhotos:Root"]=kind switch{"missing"=>null,"app"=>p.F.Factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().ContentRootPath,_=>"../photos"};
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await s.Client().PostAsJsonAsync(PhotoFixture.Root(s),PhotoFixture.Input(s,PhotoFixture.Image()))).StatusCode);Assert.Equal(0,await p.F.Read(db=>db.InspectionAttachments.CountAsync()));
    }
    [Fact] public async Task Actual_item_attempt_pair_and_removed_state_are_rechecked() {await using var p=await PhotoFixture.Create();await Scope(p);}
    internal static async Task Scope(PhotoFixture p) {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var input=PhotoFixture.Input(s,bytes);
        var other=await p.F.Read(db=>db.InspectionAttempts.AsNoTracking().SingleAsync(x=>x.PatrolTaskItemId!=s.ItemId));
        foreach(var invalid in new[]{input with{TaskItemId=other.PatrolTaskItemId},input with{InspectionAttemptId=other.Id}})
            Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(PhotoFixture.Root(s),invalid)).StatusCode);
        Assert.Equal(0,await p.F.Read(db=>db.InspectionAttachments.CountAsync()));
        var a=await PhotoFixture.Reserve(s,input);
        p.Fault.AfterStage=async()=>{await ItemRemovalTests.Accept(s,ItemRemovalTests.Input(s));};
        Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);
        Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));Assert.Equal(1,await p.F.Read(db=>db.ItemRemovalOperations.CountAsync()));
        Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Recover(s,a.AttachmentId)).StatusCode);
    }
    [Fact] public async Task Technical_photo_slots_include_pending_identities() {await using var p=await PhotoFixture.Create();await Slots(p);}
    internal static async Task Slots(PhotoFixture p) {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();
        for(var n=0;n<5;n++) Assert.False((await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes))).IsReady);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(PhotoFixture.Root(s),PhotoFixture.Input(s,bytes))).StatusCode);
        Assert.Equal(5,await p.F.Read(db=>db.InspectionAttachments.CountAsync()));Assert.Empty(Directory.GetFiles(p.DirectoryPath));await s.AssertCounts(0,0,0,0);
    }
    [Fact] public async Task Strict_manifest_rejects_paths_and_duplicate_properties() {
        await using var p=await PhotoFixture.Create();var s=await SharedDraftFixture.Setup(p.F);var input=PhotoFixture.Input(s,PhotoFixture.Image());
        var json=System.Text.Json.JsonSerializer.Serialize(input,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        foreach(var extra in new[]{"\"storageKey\":\"../escape\"","\"clientPhotoId\":\""+input.ClientPhotoId+"\""}) {
            var response=await s.Client().PostAsync(PhotoFixture.Root(s),new StringContent(json[..^1]+","+extra+"}",System.Text.Encoding.UTF8,"application/json"));
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        }
        Assert.Equal(0,await p.F.Read(db=>db.InspectionAttachments.CountAsync()));Assert.Empty(Directory.GetFiles(p.DirectoryPath));
    }
}
