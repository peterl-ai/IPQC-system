using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed record DraftSetup(PersonalSessionFixture F,Guid TaskId,Guid ItemId,Guid AttemptId,AuthOutput A,AuthOutput B,WorkContextIssued Source)
{
    public string Root=>$"/api/line-patrol/tasks/{TaskId}";
    public DraftOperationInput Capture(DraftSampleInput? value=null,Guid? client=null,WorkContextIssued? source=null,long version=0) {
        var c=source??Source;
        return new(Guid.NewGuid(),1,"CaptureSample",ItemId,AttemptId,client??Guid.NewGuid(),c.Context.Id,c.Context.UserId,version,1,
            value??new("OK",null,null),[]);
    }
    public DraftBatchInput Batch(DraftOperationInput op,long version=0,WorkContextIssued? source=null) {
        var c=source??Source;
        return new(Guid.NewGuid(),1,F.Line1,2,PersonalSessionFixture.Environment,version,[op],[],[new(c.Context.Id,c.ProofSecret)]);
    }
    public HttpClient Client(AuthOutput? user=null)=>F.Client((user??B).AccessToken);
    public async Task<DraftBatchResult> Accept(DraftBatchInput b,AuthOutput? user=null) {
        var r=await Client(user).PostAsJsonAsync(Root+"/operation-batches",b);
        Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync()); Assert.Equal("no-store",r.Headers.CacheControl!.ToString());
        return (await r.Content.ReadFromJsonAsync<DraftBatchResult>())!;
    }
    public Task<SharedDraftOutput?> Read()=>Client().GetFromJsonAsync<SharedDraftOutput>(Root+"/shared-draft");
    public DraftBatchInput Mutation(DraftOperationReceipt previous,string kind,DraftSampleInput? value,WorkContextIssued? source=null) {
        var c=source??Source;
        var op=new DraftOperationInput(Guid.NewGuid(),1,kind,ItemId,AttemptId,previous.ClientSampleId,c.Context.Id,c.Context.UserId,
            previous.DraftVersion,2,value,[new(previous.OperationId,previous.ContentHash)]);
        return Batch(op,previous.DraftVersion,c) with {AcceptedReferences=[new(previous.OperationId,previous.ContentHash)]};
    }
    public async Task AssertCounts(long version,int samples,int operations,int batches) {
        Assert.Equal(version,(await Read())!.DraftVersion);
        Assert.Equal(samples,await F.Read(db=>db.SharedSamples.CountAsync()));
        Assert.Equal(operations,await F.Read(db=>db.ContributionOperations.CountAsync()));
        Assert.Equal(batches,await F.Read(db=>db.AcceptedBatchReceipts.CountAsync()));
        Assert.Equal(batches,await F.Read(db=>db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(0,await F.Read(db=>db.PatrolTaskItemSamples.CountAsync()));
        Assert.Equal(0,await F.Read(db=>db.PatrolTaskSubmissions.CountAsync()));
        Assert.Equal(0,await F.Read(db=>db.PatrolTaskReviews.CountAsync()));
    }
}

internal static class SharedDraftFixture
{
    public static async Task<DraftSetup> Setup(PersonalSessionFixture f,bool quantitative=false,string? sampleCount=null,string? photoRequirement=null) {
        f.Clock.Now=new(2026,11,11,11,59,0,TimeSpan.Zero);
        var standard=await LineSharedPlanningFixture.Standard(f);
        if(sampleCount is not null) await f.Edit(async db=> {
            foreach(var i in await db.PatrolStandardItems.Where(x=>x.PatrolStandardId==standard).ToListAsync()) i.SampleCount=sampleCount;
        });
        if(photoRequirement is not null) await f.Edit(async db=> {
            foreach(var i in await db.PatrolStandardItems.Where(x=>x.PatrolStandardId==standard).ToListAsync()) i.PhotoRequirement=photoRequirement;
        });
        if(quantitative) await f.Edit(async db=> {
            foreach(var i in await db.PatrolStandardItems.Where(x=>x.PatrolStandardId==standard).ToListAsync()) {
                i.InspectionType="Quantitative";i.LowerLimitOperator=">=";i.LowerLimitValue="1.5";i.UpperLimitOperator="<=";i.UpperLimitValue="2.5";
            }
        });
        await LineSharedPlanningFixture.Plan(f,standard);f.Clock.Now=f.Clock.Now.AddMinutes(1);
        Assert.Equal(1,await LineSharedPlanningFixture.Generate(f));
        var task=Assert.Single(await LineSharedPlanningFixture.Tasks(f));var a=await f.Login();var b=await f.Login("dev-ipqa-2");
        var scope=(await f.Client(b.AccessToken).GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task.Id)))!;
        var item=scope.Items[0];var source=await WorkContextFixture.Issue(f,task.Id,a);
        return new(f,task.Id,item.TaskItemId,item.InspectionAttemptId,a,b,source);
    }
    public static async Task Revoke(DraftSetup s,string kind="explicit") {
        var auth=await s.F.Login("admin");
        var legacy=s.F.Client(s.F.Token(auth,c=>c.Where(x=>x.Type!="personal_session_id").ToList()));
        var r=kind switch {
            "reset"=>await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password",new ResetPasswordInput("SyntheticChanged123A")),
            "deactivate"=>await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate",null),
            "role"=>await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}",new UserUpdateInput("Synthetic inspector","PQE")),
            _=>await s.F.Client(auth.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke",new WorkContextRevokeInput(1,"Synthetic source revoke")) };
        Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());
        Assert.NotNull(await s.F.Read(db=>db.WorkContexts.Where(x=>x.Id==s.Source.Context.Id).Select(x=>x.SecurityRevokedAtUtc).SingleAsync()));
    }
}
