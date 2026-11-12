using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftConcurrencyTests
{
    [Fact] public async Task OP10_Multi_source_reverse_manifests_share_one_account_order()
    {await using var f=await PersonalSessionFixture.CreateAsync();await MultiSource(f);}
    internal static async Task MultiSource(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var bSource=await WorkContextFixture.Issue(f,s.TaskId,s.B);
        var first=s.Batch(s.Capture()) with {Operations=[s.Capture(),s.Capture(source:bSource)],SourceProofs=[new(s.Source.Context.Id,s.Source.ProofSecret),new(bSource.Context.Id,bSource.ProofSecret)]};
        var second=s.Batch(s.Capture()) with {Operations=[s.Capture(source:bSource),s.Capture()],SourceProofs=[new(bSource.Context.Id,bSource.ProofSecret),new(s.Source.Context.Id,s.Source.ProofSecret)]};
        f.Fault.Arm("INSERT INTO \"ContributionOperations\"");var one=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",first);
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));var two=s.Client(s.A).PostAsJsonAsync(s.Root+"/operation-batches",second);
        f.Fault.Release.TrySetResult();var outcomes=await Task.WhenAll(one,two);
        Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Conflict);
        await s.AssertCounts(1,2,2,1);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OP10_Actual_line_lifecycle_and_acceptance_both_orders(bool lineFirst)
    {await using var f=await PersonalSessionFixture.CreateAsync();await LineRace(f,lineFirst);}
    internal static async Task LineRace(PersonalSessionFixture f,bool lineFirst) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());var admin=f.Client((await f.Login("admin")).AccessToken);
        f.Fault.Arm(lineFirst ? "UPDATE \"ProductionLines\"" : "INSERT INTO \"ContributionOperations\"");
        var accepting=lineFirst ? null : s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);
        var line=lineFirst ? admin.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate",new ProvisioningRevisionInput(1)) : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));accepting??=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);
        line??=admin.PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate",new ProvisioningRevisionInput(1));
        f.Fault.Release.TrySetResult();var response=await accepting;Assert.Equal(HttpStatusCode.OK,(await line).StatusCode);
        Assert.False(await f.Read(db=>db.ProductionLines.Where(x=>x.Id==f.Line1).Select(x=>x.IsActive).SingleAsync()));
        Assert.Equal(lineFirst ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(lineFirst?0:1,await f.Read(db=>db.ContributionOperations.CountAsync()));
    }
    [Fact] public async Task OP07_Two_independent_batches_have_one_CAS_winner()
    {await using var f=await PersonalSessionFixture.CreateAsync();await Cas(f);}
    internal static async Task Cas(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var a=s.Batch(s.Capture());var b=s.Batch(s.Capture());
        f.Fault.Arm("UPDATE \"SharedDrafts\"");var first=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",a);
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));var second=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);
        f.Fault.Release.TrySetResult();var outcomes=await Task.WhenAll(first,second);
        Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Conflict);
        await s.AssertCounts(1,1,1,1);
    }
    [Fact] public async Task OP08_Early_NotFound_then_late_commit_recovers_one_original_outcome()
    {await using var f=await PersonalSessionFixture.CreateAsync();await LostResponse(f);}
    internal static async Task LostResponse(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        // Pause the real request's current-family middleware before its transaction.
        // SQLite reserves its global writer before source discovery; pausing inside
        // that transaction would block the receipt GET rather than return NotFound.
        f.Fault.Arm("FROM \"RefreshTokenSessions\"");var accepting=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);
        HttpResponseMessage response;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.NotFound,(await s.Client(s.A).GetAsync(s.Root+$"/operation-batches/{b.BatchId}/receipt")).StatusCode);
        } finally {f.Fault.Release.TrySetResult();response=await accepting;}
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var original=(await response.Content.ReadFromJsonAsync<DraftBatchResult>())!; // simulate losing this response at the client
        var queried=await s.Client().GetFromJsonAsync<DraftBatchReceipt>(s.Root+$"/operation-batches/{b.BatchId}/receipt");
        Assert.Equal(original.BatchReceipt!.RequestFingerprint,queried!.RequestFingerprint);
        Assert.Equal(Assert.Single(original.Operations),Assert.Single((await s.Accept(b with {SourceProofs=[]},s.A)).Operations));
        await s.AssertCounts(1,1,1,1);
    }
    [Theory][InlineData(false,"explicit")][InlineData(true,"explicit")][InlineData(false,"deactivate")][InlineData(true,"deactivate")]
    [InlineData(false,"role")][InlineData(true,"role")][InlineData(false,"reset")][InlineData(true,"reset")]
    public async Task OP06_Account_and_source_security_writers_serialize(bool revokeFirst,string action)
    {await using var f=await PersonalSessionFixture.CreateAsync();await SecurityRace(f,revokeFirst,action);}
    internal static async Task SecurityRace(PersonalSessionFixture f,bool revokeFirst,string action) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        f.Fault.Arm(revokeFirst ? action=="explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"ContributionOperations\"");
        Task<HttpResponseMessage>? accepted=revokeFirst ? null : s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);
        Task? security=revokeFirst ? SharedDraftFixture.Revoke(s,action) : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        accepted??=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);security??=SharedDraftFixture.Revoke(s,action);
        f.Fault.Release.TrySetResult();var r=await accepted;await security;
        Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK,r.StatusCode);
        if(!revokeFirst) {
            var receipt=Assert.Single((await r.Content.ReadFromJsonAsync<DraftBatchResult>())!.Operations);
            Assert.Equal(receipt,(await s.Client().GetFromJsonAsync<DraftOperationReceipt>(s.Root+$"/operations/{receipt.OperationId}/receipt"))!);
        }
        await s.AssertCounts(revokeFirst?0:1,revokeFirst?0:1,revokeFirst?0:1,revokeFirst?0:1);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OP10_Task_editability_transition_serializes_with_acceptance(bool stateFirst)
    {await using var f=await PersonalSessionFixture.CreateAsync();await StateRace(f,stateFirst);}
    internal static async Task StateRace(PersonalSessionFixture f,bool stateFirst) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        using var scope=f.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<JaxPower.Ipqc.Api.Data.IpqcDbContext>();
        var store=scope.ServiceProvider.GetRequiredService<WorkContextStore>();
        Task<int> Change()=>store.RunAsync(WorkContextFixture.Principal(s.B),false,s.TaskId,[],async _=>
            await db.PatrolTasks.Where(x=>x.Id==s.TaskId).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.Status,"Completed")),default);
        f.Fault.Arm(stateFirst ? "UPDATE \"PatrolTasks\"" : "INSERT INTO \"ContributionOperations\"");
        var accepted=stateFirst ? null : s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);var changed=stateFirst ? Change() : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));accepted??=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);changed??=Change();
        f.Fault.Release.TrySetResult();Assert.Equal(1,await changed);var r=await accepted;
        Assert.Equal(stateFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK,r.StatusCode);
        Assert.Equal("Completed",await f.Read(d=>d.PatrolTasks.Where(x=>x.Id==s.TaskId).Select(x=>x.Status).SingleAsync()));
        await s.AssertCounts(stateFirst?0:1,stateFirst?0:1,stateFirst?0:1,stateFirst?0:1);
    }
    [Theory][InlineData(false,"logout")][InlineData(true,"logout")][InlineData(false,"refresh")][InlineData(true,"refresh")]
    public async Task OP10_Current_B_family_rotation_and_logout(bool sessionFirst,string action)
    {await using var f=await PersonalSessionFixture.CreateAsync();await FamilyRace(f,sessionFirst,action);}
    internal static async Task FamilyRace(PersonalSessionFixture f,bool sessionFirst,string action) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        async Task Change() {if(action=="logout") Assert.Equal(HttpStatusCode.NoContent,(await s.Client().PostAsJsonAsync("/api/auth/logout",new RefreshInput(s.B.RefreshToken))).StatusCode);
            else Assert.Equal(s.B.PersonalSessionId,(await f.Refresh(s.B.RefreshToken!)).PersonalSessionId);}
        f.Fault.Arm(sessionFirst ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"ContributionOperations\"");
        var accepted=sessionFirst ? null : s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);var change=sessionFirst ? Change() : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));accepted??=s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);change??=Change();
        f.Fault.Release.TrySetResult();var response=await accepted;await change;
        var denied=sessionFirst && action=="logout";Assert.Equal(denied ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,response.StatusCode);
        var current=await f.Login("dev-ipqa-2");s=s with {B=current};await s.AssertCounts(denied?0:1,denied?0:1,denied?0:1,denied?0:1);
    }
}
