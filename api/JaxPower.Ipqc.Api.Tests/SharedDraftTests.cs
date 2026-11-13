using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftTests
{
    [Fact] public async Task OP01_03_12_Capture_correct_remove_and_exact_receipt_recovery()
    {await using var f=await PersonalSessionFixture.CreateAsync();await Lifecycle(f);}
    internal static async Task Lifecycle(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);
        var initial=(await s.Read())!;Assert.Equal(0,initial.DraftVersion);Assert.Empty(initial.Samples);Assert.False(initial.CanExecute);
        Assert.Equal(0,await f.Read(db=>db.SharedDrafts.CountAsync()));
        var op=s.Capture();var batch=s.Batch(op);var accepted=await s.Accept(batch);var first=Assert.Single(accepted.Operations);
        Assert.NotNull(accepted.BatchReceipt);Assert.Equal(1,accepted.BatchReceipt.DraftVersion);Assert.False(accepted.AcceptsFormalInspection);
        Assert.Equal(TestAccounts.Ipqa1Id,first.ContributorUserId);Assert.Equal(TestAccounts.Ipqa2Id,first.UploadedByUserId);
        Assert.Equal(s.B.PersonalSessionId,first.UploadedPersonalSessionId);Assert.NotEqual(op.ClientSampleId,first.SampleId);
        var replay=await s.Accept(batch with {SourceProofs=[]},s.A);Assert.True(replay.ReplayOnly);Assert.Equal(first,Assert.Single(replay.Operations));
        var only=await s.Accept(batch with {BatchId=Guid.NewGuid(),ExpectedDraftVersion=99,SourceProofs=[]});
        Assert.True(only.ReplayOnly);Assert.Null(only.BatchReceipt);Assert.Equal(first,Assert.Single(only.Operations));
        await s.AssertCounts(1,1,1,1);
        var changed=batch with {Operations=[op with {Data=new("NG",null,null)}]};
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",changed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",changed with {BatchId=Guid.NewGuid()})).StatusCode);
        var sourceB=await WorkContextFixture.Issue(f,s.TaskId,s.B);
        var changedSource=batch with {BatchId=Guid.NewGuid(),ExpectedDraftVersion=1,Operations=[op with {
            SourceWorkContextId=sourceB.Context.Id,ContributorUserIdAssertion=sourceB.Context.UserId}],SourceProofs=[new(sourceB.Context.Id,sourceB.ProofSecret)]};
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",changedSource)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",batch with {ExpectedDraftVersion=1})).StatusCode);
        var correction=s.Mutation(first,"CorrectSample",new("NG",null,null),sourceB);
        var corrected=Assert.Single((await s.Accept(correction,s.A)).Operations,x=>x.OperationId==correction.Operations[0].OperationId);
        Assert.Equal(first.SampleId,corrected.SampleId);Assert.Equal(TestAccounts.Ipqa2Id,corrected.ContributorUserId);Assert.Equal(TestAccounts.Ipqa1Id,corrected.UploadedByUserId);
        var removal=s.Mutation(corrected,"RemoveSample",null);
        var removed=Assert.Single((await s.Accept(removal)).Operations,x=>x.OperationId==removal.Operations[0].OperationId);
        Assert.True(removed.IsRemoved);Assert.Null(removed.Value);Assert.Equal(corrected.OperationId,removed.ValueOperationId);
        var projection=Assert.Single((await s.Read())!.Samples);Assert.True(projection.IsRemoved);
        Assert.Equal("NG",projection.Value.Judgment);Assert.Equal(corrected.OperationId,projection.ValueOperationId);
        Assert.Equal(TestAccounts.Ipqa2Id,projection.ValueContributorUserId);Assert.Equal(removed.OperationId,projection.LastOperationId);
        await s.Accept(batch with {BatchId=Guid.NewGuid(),SourceProofs=[]});
        Assert.True(Assert.Single((await s.Read())!.Samples).IsRemoved);
        await s.AssertCounts(3,1,3,3);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",s.Batch(s.Capture(client:op.ClientSampleId,version:3),3))).StatusCode);
        var fresh=await s.Accept(s.Batch(s.Capture(version:3),3));Assert.NotEqual(first.SampleId,Assert.Single(fresh.Operations).SampleId);
        await s.AssertCounts(4,2,4,4);
        Assert.Equal(first,(await s.Client().GetFromJsonAsync<DraftOperationReceipt>(s.Root+$"/operations/{first.OperationId}/receipt"))!);
        var receipt=await s.Client().GetFromJsonAsync<DraftBatchReceipt>(s.Root+$"/operation-batches/{batch.BatchId}/receipt");Assert.Equal(first,Assert.Single(receipt!.Operations));
        var task=await f.Read(db=>db.PatrolTasks.AsNoTracking().SingleAsync(x=>x.Id==s.TaskId));Assert.Equal("PendingInspection",task.Status);Assert.Null(task.StartedAtUtc);Assert.Equal(0,task.CurrentRevisionNo);
    }

    [Theory][InlineData(1.5,"OK")][InlineData(2.5,"OK")][InlineData(1.49,"NG")][InlineData(2.51,"NG")][InlineData(0,"NG")]
    public async Task OP01_Frozen_quantitative_values_are_real_and_server_judged(double number,string expected) {
        await using var f=await PersonalSessionFixture.CreateAsync();await Quantitative(f,(decimal)number,expected);
    }
    internal static async Task Quantitative(PersonalSessionFixture f,decimal number,string expected) {
        var s=await SharedDraftFixture.Setup(f,true);
        // Today's mutable Standard must not change the generation-time numeric bounds.
        await f.Edit(async db=> {foreach(var i in await db.PatrolStandardItems.ToListAsync()) i.UpperLimitValue="999";});
        var accepted=await s.Accept(s.Batch(s.Capture(new(null,number,"same barcode"))));
        var r=Assert.Single(accepted.Operations);Assert.Equal(expected,r.Value!.Judgment);Assert.Equal(number,r.Value.InspectionValue);
        Assert.Equal("same barcode",r.Value.Barcode);Assert.Equal("NotEvaluatedForFormalSubmission",(await s.Read())!.Completeness);
        var equal=Assert.Single((await s.Accept(s.Batch(s.Capture(new(null,number,"same barcode"),version:1),1))).Operations);
        Assert.NotEqual(r.SampleId,equal.SampleId);Assert.Equal(r.Value,equal.Value);await s.AssertCounts(2,2,2,2);
    }

    [Theory][InlineData("qual-value")][InlineData("qual-barcode")][InlineData("qual-null")][InlineData("quant-label")][InlineData("quant-null")]
    public async Task OP01_Invalid_typed_sample_never_creates_a_placeholder(string kind) {
        await using var f=await PersonalSessionFixture.CreateAsync();var s=await SharedDraftFixture.Setup(f,kind.StartsWith("quant",StringComparison.Ordinal));
        var data=kind switch {"qual-value"=>new DraftSampleInput("OK",0,null),"qual-barcode"=>new("OK",null,"x"),"qual-null"=>new(null,null,null),"quant-label"=>new("OK",2,null),_=>new(null,null,null)};
        var r=await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",s.Batch(s.Capture(data)));Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        await s.AssertCounts(0,0,0,0);Assert.Equal(0,await f.Read(db=>db.SharedDrafts.CountAsync()));
    }
}
