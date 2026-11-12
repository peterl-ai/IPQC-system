using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftRollbackTests
{
    [Theory][InlineData("INSERT INTO \"SharedDrafts\"")][InlineData("UPDATE \"SharedDrafts\"")]
    [InlineData("INSERT INTO \"SharedSamples\"")][InlineData("INSERT INTO \"AcceptedBatchReceipts\"")]
    [InlineData("INSERT INTO \"ContributionOperations\"")][InlineData("INSERT INTO \"SharedSampleProjections\"")]
    [InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task OP09_First_acceptance_fault_rolls_back_every_fact(string point)
    {await using var f=await PersonalSessionFixture.CreateAsync();await Fresh(f,point);}
    internal static async Task Fresh(PersonalSessionFixture f,string point) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        if(point=="commit") f.CommitFault.Armed=true;else f.Fault.Arm(point,true);
        var failed=await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);Assert.Equal(HttpStatusCode.InternalServerError,failed.StatusCode);
        if(point=="commit") Assert.False(f.CommitFault.Armed);else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        await s.AssertCounts(0,0,0,0);Assert.Equal(0,await f.Read(db=>db.SharedSampleProjections.CountAsync()));
        Assert.Equal(0,await f.Read(db=>db.SharedDrafts.CountAsync()));
        Assert.Equal(HttpStatusCode.NotFound,(await s.Client().GetAsync(s.Root+$"/operation-batches/{b.BatchId}/receipt")).StatusCode);
        await s.Accept(b);await s.AssertCounts(1,1,1,1);
    }
    [Theory][InlineData("INSERT INTO \"ContributionDependencies\"")][InlineData("UPDATE \"SharedSampleProjections\"")]
    [InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task OP09_Failed_correction_preserves_prior_accepted_work(string point)
    {await using var f=await PersonalSessionFixture.CreateAsync();await Correction(f,point);}
    internal static async Task Correction(PersonalSessionFixture f,string point) {
        var s=await SharedDraftFixture.Setup(f);var first=Assert.Single((await s.Accept(s.Batch(s.Capture()))).Operations);
        var next=s.Mutation(first,"CorrectSample",new("NG",null,null));
        if(point=="commit") f.CommitFault.Armed=true;else f.Fault.Arm(point,true);
        Assert.Equal(HttpStatusCode.InternalServerError,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",next)).StatusCode);
        if(point=="commit") Assert.False(f.CommitFault.Armed);else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        await s.AssertCounts(1,1,1,1);var preserved=Assert.Single((await s.Read())!.Samples);
        Assert.Equal(first.OperationId,preserved.ValueOperationId);Assert.Equal("OK",preserved.Value.Judgment);
        Assert.Equal(0,await f.Read(db=>db.ContributionDependencies.CountAsync()));
        Assert.Equal(HttpStatusCode.NotFound,(await s.Client().GetAsync(s.Root+$"/operation-batches/{next.BatchId}/receipt")).StatusCode);
        await s.Accept(next);await s.AssertCounts(2,1,2,2);
    }
}
