using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftSecurityTests
{
    [Theory][InlineData("logout")][InlineData("self-password")][InlineData("expiry")][InlineData("refresh")]
    public async Task OP05_Normal_A_session_changes_do_not_reject_saved_sources(string action)
    {await using var f=await PersonalSessionFixture.CreateAsync();await Normal(f,action);}
    internal static async Task Normal(PersonalSessionFixture f,string action) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        if(action=="logout") Assert.Equal(HttpStatusCode.NoContent,(await s.Client(s.A).PostAsJsonAsync("/api/auth/logout",new RefreshInput(s.A.RefreshToken))).StatusCode);
        if(action=="self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password",new ChangePasswordInput(TestAccounts.Password,"SyntheticChanged123A"))).EnsureSuccessStatusCode();
        if(action=="expiry") f.Clock.Now=f.Clock.Now.AddDays(15);
        if(action=="refresh") {var rotated=await f.Refresh(s.A.RefreshToken!);Assert.Equal(s.A.PersonalSessionId,rotated.PersonalSessionId);}
        var currentB=await f.Login("dev-ipqa-2");s=s with {B=currentB};var result=await s.Accept(b,currentB);
        var r=Assert.Single(result.Operations);Assert.Equal(s.A.User.Id,r.ContributorUserId);Assert.Equal(currentB.User.Id,r.UploadedByUserId);
        Assert.Null(await f.Read(db=>db.WorkContexts.Where(x=>x.Id==s.Source.Context.Id).Select(x=>x.SecurityRevokedAtUtc).SingleAsync()));
        await s.AssertCounts(1,1,1,1);
    }
    [Theory][InlineData("reset")][InlineData("role")][InlineData("deactivate")][InlineData("explicit")]
    public async Task OP05_06_Revocation_rejects_new_work_and_preserves_accepted_receipts(string action)
    {await using var f=await PersonalSessionFixture.CreateAsync();await Security(f,action);}
    internal static async Task Security(PersonalSessionFixture f,string action) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());var first=Assert.Single((await s.Accept(b)).Operations);
        await SharedDraftFixture.Revoke(s,action);
        if(action is "deactivate" or "role") {
            var admin=await f.Login("admin");var legacy=f.Client(f.Token(admin,cs=>cs.Where(c=>c.Type!=PersonalAccess.FamilyClaim).ToList()));
            var restored=action=="deactivate" ? await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/activate",null) :
                await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}",new UserUpdateInput("Synthetic inspector","IPQA"));
            Assert.True(restored.IsSuccessStatusCode,await restored.Content.ReadAsStringAsync());
            Assert.True(await f.Read(db=>db.ApplicationUsers.AnyAsync(x=>x.Id==TestAccounts.Ipqa1Id && x.IsActive && x.Role=="IPQA")));
        }
        var replay=await s.Accept(b with {SourceProofs=[]});Assert.Equal(first,Assert.Single(replay.Operations));
        var newBatch=s.Batch(s.Capture(version:1),1);var bad=await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",newBatch);
        Assert.Equal(HttpStatusCode.Conflict,bad.StatusCode);var json=JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("SourceSecurityRevoked",json.GetProperty("code").GetString());Assert.True(json.GetProperty("quarantineRequired").GetBoolean());
        Assert.Equal(newBatch.Operations[0].OperationId,json.GetProperty("operationIds")[0].GetGuid());
        var sourceB=await WorkContextFixture.Issue(f,s.TaskId,s.B);
        var mixed=newBatch with {Operations=[newBatch.Operations[0],s.Capture(source:sourceB,version:1)],SourceProofs=[..newBatch.SourceProofs,new(sourceB.Context.Id,sourceB.ProofSecret)]};
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",mixed)).StatusCode);
        await s.AssertCounts(1,1,1,1);
        var correction=s.Mutation(first,"CorrectSample",new("NG",null,null),sourceB);
        var accepted=await s.Accept(correction);var corrected=Assert.Single(accepted.Operations,x=>x.OperationId==correction.Operations[0].OperationId);
        Assert.Equal(TestAccounts.Ipqa2Id,corrected.ContributorUserId);Assert.Equal(first,Assert.Single(accepted.Operations,x=>x.OperationId==first.OperationId));
        await s.AssertCounts(2,1,2,2);
        var projection=Assert.Single((await s.Read())!.Samples);Assert.Equal(corrected.OperationId,projection.ValueOperationId);
    }
    [Theory][InlineData("proof")][InlineData("missing")][InlineData("owner")][InlineData("unknown")]
    public async Task OP02_Invalid_source_quarantines_dependent_operations_atomically(string action)
    {await using var f=await PersonalSessionFixture.CreateAsync();await Mixed(f,action);}
    internal static async Task Mixed(PersonalSessionFixture f,string action) {
        var s=await SharedDraftFixture.Setup(f);var capture=s.Capture();var b=s.Batch(capture);
        var hash=DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId,b,capture));
        var cB=await WorkContextFixture.Issue(f,s.TaskId,s.B);
        var correction=capture with {OperationId=Guid.NewGuid(),Kind="CorrectSample",SourceWorkContextId=cB.Context.Id,
            ContributorUserIdAssertion=cB.Context.UserId,Data=new("NG",null,null),Dependencies=[new(capture.OperationId,hash)]};
        b=b with {Operations=[capture,correction],SourceProofs=[new(s.Source.Context.Id,s.Source.ProofSecret),new(cB.Context.Id,cB.ProofSecret)]};
        if(action=="proof") b=b with {SourceProofs=[new(s.Source.Context.Id,new string('x',43)),new(cB.Context.Id,cB.ProofSecret)]};
        if(action=="missing") b=b with {SourceProofs=[new(cB.Context.Id,cB.ProofSecret)]};
        if(action=="owner") {
            capture=capture with {ContributorUserIdAssertion=TestAccounts.Ipqa2Id};
            correction=correction with {Dependencies=[new(capture.OperationId,DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId,b,capture)))]};
            b=b with {Operations=[capture,correction]};
        }
        if(action=="unknown") {
            var unknown=Guid.NewGuid();capture=capture with {SourceWorkContextId=unknown};
            correction=correction with {Dependencies=[new(capture.OperationId,DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId,b,capture)))]};
            b=b with {Operations=[capture,correction],SourceProofs=[new(unknown,s.Source.ProofSecret),new(cB.Context.Id,cB.ProofSecret)]};
        }
        var r=await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);Assert.Equal(HttpStatusCode.Conflict,r.StatusCode);
        var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("SourceProvenanceConflict",json.GetProperty("code").GetString());Assert.True(json.GetProperty("preserveOriginals").GetBoolean());
        Assert.Equal(new[]{capture.OperationId,correction.OperationId}.Order(),json.GetProperty("operationIds").EnumerateArray().Select(x=>x.GetGuid()).Order());
        await s.AssertCounts(0,0,0,0);Assert.Equal(0,await f.Read(db=>db.SharedDrafts.CountAsync()));
    }
    [Fact] public async Task OP04_HTTP_and_direct_entrypoints_require_current_authority()
    {await using var f=await PersonalSessionFixture.CreateAsync();await Authority(f);}
    internal static async Task Authority(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());var url=s.Root+"/operation-batches";
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Anonymous.PostAsync(url,new StringContent("{broken",Encoding.UTF8,"application/json"))).StatusCode);
        foreach(var name in new[]{"admin","pqe"}) Assert.Equal(HttpStatusCode.Forbidden,(await f.Client((await f.Login(name)).AccessToken).PostAsJsonAsync(url,b)).StatusCode);
        var noFamily=f.Token(s.B,cs=>cs.Where(c=>c.Type!=PersonalAccess.FamilyClaim).ToList());
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client(noFamily).PostAsJsonAsync(url,b)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await f.Client(s.Source.ProofSecret).PostAsJsonAsync(url,b)).StatusCode);
        using var scope=f.Factory.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<SharedDraftService>();
        var fake=new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(s.B).Claims));
        async Task Deny(Func<Task> call) {var ex=await Assert.ThrowsAsync<PersonalAccessException>(call);Assert.Equal(401,ex.Status);}
        await Deny(async()=>await service.AcceptAsync(s.TaskId,b,fake,default));
        await Deny(async()=>await service.ReadAsync(s.TaskId,fake,default));
        await Deny(async()=>await service.BatchReceiptAsync(s.TaskId,b.BatchId,fake,default));
        await Deny(async()=>await service.OperationReceiptAsync(s.TaskId,b.Operations[0].OperationId,fake,default));
        var noFamilyPrincipal=new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(s.B).Claims.Where(c=>c.Type!=PersonalAccess.FamilyClaim),"synthetic"));
        await Deny(async()=>await service.AcceptAsync(s.TaskId,b,noFamilyPrincipal,default));
        foreach(var name in new[]{"admin","pqe"}) {
            var role=await f.Login(name);
            var denied=await Assert.ThrowsAsync<PersonalAccessException>(()=>service.AcceptAsync(s.TaskId,b,WorkContextFixture.Principal(role),default));
            Assert.Equal(403,denied.Status);
        }
        await f.Edit(async db=>(await db.ApplicationUsers.SingleAsync(x=>x.Id==s.B.User.Id)).MustChangePassword=true);
        var forced=await Assert.ThrowsAsync<PersonalAccessException>(()=>service.AcceptAsync(s.TaskId,b,WorkContextFixture.Principal(s.B),default));
        Assert.Equal(403,forced.Status);
        Assert.Equal(HttpStatusCode.Forbidden,(await s.Client().PostAsync(url,new StringContent("{broken",Encoding.UTF8,"application/json"))).StatusCode);
        await f.Edit(async db=>(await db.ApplicationUsers.SingleAsync(x=>x.Id==s.B.User.Id)).MustChangePassword=false);
        await s.AssertCounts(0,0,0,0);
        var accepted=await s.Accept(b);var op=Assert.Single(accepted.Operations);
        await f.Edit(async db=>(await db.PatrolTasks.SingleAsync(x=>x.Id==s.TaskId)).Status="Completed");
        Assert.Equal(op,(await s.Client().GetFromJsonAsync<DraftOperationReceipt>(s.Root+$"/operations/{op.OperationId}/receipt"))!);
        Assert.Equal(op,Assert.Single((await s.Accept(b with {SourceProofs=[]})).Operations));
        var forbidden=await s.Client().PostAsJsonAsync(url,s.Batch(s.Capture(version:1),1));Assert.Equal(HttpStatusCode.Conflict,forbidden.StatusCode);
        Assert.Equal(1,await f.Read(db=>db.SharedDrafts.Select(x=>x.DraftVersion).SingleAsync()));
    }
}
