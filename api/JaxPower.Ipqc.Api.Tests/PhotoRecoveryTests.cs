using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoRecoveryTests
{
    [Theory][InlineData("disk")][InlineData("stream")][InlineData("stored")][InlineData("rename")][InlineData("after-rename")][InlineData("commit")][InlineData("audit")]
    [InlineData("crash-receiving")]
    public async Task Failures_and_fresh_host_recovery(string point) {await using var p=await PhotoFixture.Create();await Failure(p,point);}
    internal static async Task Failure(PhotoFixture p,string point)
    {
        var s=await SharedDraftFixture.Setup(p.F);await s.Accept(s.Batch(s.Capture()));
        var retained=await ItemRemovalTests.Retained(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));var reached=false;
        switch(point) {
            case "disk":p.Fault.BeforeStage=()=>{reached=true;throw new PhotoStorageException("PhotoStorageWriteFailed");};break;
            case "stream":p.Fault.AfterStage=async()=>{reached=true;var key=await p.F.Read(db=>db.AttachmentUploads.Select(x=>x.StageKey).SingleAsync());await File.WriteAllBytesAsync(Path.Combine(p.DirectoryPath,key+".stage"),bytes[..10]);throw new PhotoStorageException("PhotoStorageWriteFailed");};break;
            case "stored":p.Fault.AfterStage=()=>{reached=true;p.F.Fault.Arm("UPDATE \"AttachmentUploads\"",true);return Task.CompletedTask;};break;
            case "rename":p.Fault.BeforePromote=()=>{reached=true;throw new PhotoStorageException("PhotoStoragePromoteFailed");};break;
            case "after-rename":p.Fault.AfterPromote=()=>{reached=true;throw new PhotoStorageException("PhotoStoragePromoteFailed");};break;
            case "commit":p.Fault.AfterPromote=()=>{reached=true;p.F.CommitFault.Armed=true;};break;
            case "audit":p.Fault.AfterPromote=()=>{reached=true;p.F.Fault.Arm("INSERT INTO \"AttachmentEvents\"",true);};break;
            case "crash-receiving":p.Fault.AfterStage=()=>{reached=true;p.F.Fault.Arm("FROM \"RefreshTokenSessions\"",true);throw new PhotoStorageException("PhotoStorageWriteFailed");};break;
        }
        var failed=await PhotoFixture.Upload(s,a.AttachmentId,bytes);Assert.False(failed.IsSuccessStatusCode);Assert.True(reached);
        Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));Assert.Equal(retained,await ItemRemovalTests.Retained(p.F));
        if(point=="crash-receiving") {Assert.Equal("Receiving",await p.F.Read(db=>db.AttachmentAvailabilities.Select(x=>x.State).SingleAsync()));p.F.Clock.Now=p.F.Clock.Now.AddMinutes(11);}
        // Recreate ALL photo services and file adapter in a fresh application host over the same DB/volume.
        await using var restarted=p.F.Factory.WithWebHostBuilder(web=> {
            web.UseSetting("InspectionPhotos:Root",p.DirectoryPath);
            web.ConfigureTestServices(services=> {
                services.RemoveAll<IProtectedPhotoStore>();services.AddSingleton<IProtectedPhotoStore,ProtectedPhotoStore>();
            });
        });
        var client=restarted.CreateClient();
        var login=await client.PostAsJsonAsync("/api/auth/login",new LoginInput("dev-ipqa-2",TestAccounts.Password,"Native"));login.EnsureSuccessStatusCode();
        var fresh=(await login.Content.ReadFromJsonAsync<AuthOutput>())!;client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",fresh.AccessToken);
        var recover=new HttpRequestMessage(HttpMethod.Post,PhotoFixture.Root(s)+$"/{a.AttachmentId}/recover");recover.Headers.Add("X-WorkContext-Proof",s.Source.ProofSecret);
        var response=await client.SendAsync(recover);
        if(point is "disk" or "stream") {
            Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
            var retry=new HttpRequestMessage(HttpMethod.Put,PhotoFixture.Root(s)+$"/{a.AttachmentId}/content"){Content=new ByteArrayContent(bytes)};
            retry.Headers.Add("X-WorkContext-Proof",s.Source.ProofSecret);retry.Content.Headers.ContentType=new("application/octet-stream");response=await client.SendAsync(retry);
        }
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());var ready=(await response.Content.ReadFromJsonAsync<AttachmentOutput>())!;Assert.True(ready.IsReady);Assert.Equal(fresh.PersonalSessionId,ready.UploadedPersonalSessionId);
        Assert.Equal(bytes,await client.GetByteArrayAsync(PhotoFixture.Root(s)+$"/{a.AttachmentId}/content"));Assert.Equal(1,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
        Assert.Equal(retained,await ItemRemovalTests.Retained(p.F));
    }
    [Fact] public async Task Interrupted_input_never_ready() {
        await using var p=await PhotoFixture.Create();var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));
        using var scope=p.F.Factory.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<InspectionAttachmentService>();
        var claims=new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(s.B.AccessToken).Claims.ToList();
        claims.Add(new(System.Security.Claims.ClaimTypes.Role,"IPQA"));claims.Add(new("must_change_password","false"));
        var principal=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims,"test"));
        var proofFailure=await Assert.ThrowsAsync<PersonalAccessException>(()=>service.Upload(s.TaskId,a.AttachmentId,"invalid",new BrokenStream(),principal,default));
        Assert.Equal("SourceProvenanceConflict",proofFailure.Message);Assert.Empty(Directory.GetFiles(p.DirectoryPath));
        await Assert.ThrowsAsync<PhotoStorageException>(()=>service.Upload(s.TaskId,a.AttachmentId,s.Source.ProofSecret,new BrokenStream(),principal,default));
        Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));Assert.Equal("Failed",await p.F.Read(db=>db.AttachmentAvailabilities.Select(x=>x.State).SingleAsync()));
    }
    private sealed class BrokenStream : MemoryStream {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>throw new IOException("Synthetic interrupted input");
    }
    [Fact] public async Task Ready_commit_lost_response_returns_original_acceptance() {await using var p=await PhotoFixture.Create();await LostResponse(p);}
    internal static async Task LostResponse(PhotoFixture p) {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));var committed=false;
        p.Fault.AfterPromote=()=>p.F.CommitFault.AfterCommit=()=>{committed=true;throw new IOException("Synthetic response lost after durable commit");};
        Assert.Equal(HttpStatusCode.InternalServerError,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);Assert.True(committed);
        var recovered=(await s.Client().GetFromJsonAsync<AttachmentOutput>(PhotoFixture.Root(s)+$"/by-client/{a.ClientPhotoId}"))!;Assert.True(recovered.IsReady);
        Assert.Equal(recovered,await PhotoFixture.Ready(s,a.AttachmentId,bytes));Assert.Equal(recovered,await (await PhotoFixture.Recover(s,a.AttachmentId,"")).Content.ReadFromJsonAsync<AttachmentOutput>());
        Assert.Equal(1,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));Assert.Equal(bytes,await File.ReadAllBytesAsync(await p.FinalPath(a.AttachmentId)));
    }
}
