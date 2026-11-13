using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftManifestTests
{
    [Theory][InlineData("missing")][InlineData("duplicate")][InlineData("cycle")][InlineData("foreign-attempt")]
    [InlineData("standard-item")][InlineData("unknown-attempt")][InlineData("duplicate-op")][InlineData("duplicate-client")]
    [InlineData("invented-ref")][InlineData("stale-base")][InlineData("unsupported-version")][InlineData("extra-proof")]
    [InlineData("wrong-hash")][InlineData("extra-reference")][InlineData("wrong-sample")]
    public async Task OP02_Manifest_rejects_without_partial_effects(string kind)
    {await using var f=await PersonalSessionFixture.CreateAsync();await Negative(f,kind);}
    internal static async Task Negative(PersonalSessionFixture f,string kind) {
        var s=await SharedDraftFixture.Setup(f);var op=s.Capture();var b=s.Batch(op);int prior=0;
        if(kind is "missing" or "wrong-hash" or "extra-reference" or "wrong-sample") {
            var old=Assert.Single((await s.Accept(b)).Operations);prior=1;
            b=s.Mutation(old,"CorrectSample",new("NG",null,null));op=b.Operations[0];
            if(kind=="missing") b=b with {AcceptedReferences=[]};
            if(kind=="wrong-hash") b=b with {AcceptedReferences=[new(old.OperationId,new string('A',64))]};
            if(kind=="extra-reference") b=s.Batch(s.Capture(version:1),1) with {AcceptedReferences=[new(old.OperationId,old.ContentHash)]};
            if(kind=="wrong-sample") b=b with {Operations=[op with {ClientSampleId=Guid.NewGuid()}]};
        }
        if(kind=="duplicate") {var d=new DraftOperationReference(Guid.NewGuid(),new string('A',64));b=b with {Operations=[op with {Dependencies=[d,d]}]};}
        if(kind=="cycle") {
            var second=s.Capture();b=b with {Operations=[op with {Dependencies=[new(second.OperationId,new string('A',64))]},second with {Dependencies=[new(op.OperationId,new string('A',64))]}]};
        }
        if(kind=="foreign-attempt") {
            var scopes=(await s.Client().GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(s.TaskId)))!;
            b=b with {Operations=[op with {InspectionAttemptId=scopes.Items.Single(x=>x.TaskItemId!=op.TaskItemId).InspectionAttemptId}]};
        }
        if(kind=="standard-item") {var item=(await LineSharedPlanningFixture.Tasks(f)).Single().Items.Single(x=>x.Id==op.TaskItemId);b=b with {Operations=[op with {TaskItemId=item.SourcePatrolStandardItemId}]};}
        if(kind=="unknown-attempt") b=b with {Operations=[op with {InspectionAttemptId=Guid.NewGuid()}]};
        if(kind=="duplicate-op") b=b with {Operations=[op,op]};
        if(kind=="duplicate-client") b=b with {Operations=[op,op with {OperationId=Guid.NewGuid()}]};
        if(kind=="invented-ref") b=b with {AcceptedReferences=[new(Guid.NewGuid(),new string('A',64))]};
        if(kind=="stale-base") b=b with {Operations=[op with {BaseDraftVersion=1}]};
        if(kind=="unsupported-version") b=b with {Operations=[op with {CanonicalizationVersion=2}]};
        if(kind=="extra-proof") {var extra=await WorkContextFixture.Issue(f,s.TaskId,s.B);b=b with {SourceProofs=[..b.SourceProofs,new(extra.Context.Id,extra.ProofSecret)]};}
        var r=await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b);
        Assert.Equal(kind is "duplicate" or "cycle" or "duplicate-op" or "unsupported-version" or "extra-proof" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict,r.StatusCode);
        if(kind=="cycle") Assert.Contains("CyclicDependency",await r.Content.ReadAsStringAsync());
        await s.AssertCounts(prior,prior,prior,prior);
        if(prior>0) Assert.Equal("OK",Assert.Single((await s.Read())!.Samples).Value.Judgment);
    }
    [Fact] public async Task OP01_02_Real_same_batch_dependencies_and_equal_values_are_allowed() {
        await using var f=await PersonalSessionFixture.CreateAsync();var s=await SharedDraftFixture.Setup(f);var a=s.Capture();var b=s.Batch(a);
        var hash=DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId,b,a));
        var correction=a with {OperationId=Guid.NewGuid(),Kind="CorrectSample",Data=new("NG",null,null),Dependencies=[new(a.OperationId,hash)]};
        b=b with {Operations=[correction,a,s.Capture()]};
        var r=await s.Accept(b);Assert.Equal(3,r.Operations.Count);await s.AssertCounts(1,2,3,1);
        Assert.Equal(2,(await s.Read())!.Samples.Count);Assert.Equal("NG",(await s.Read())!.Samples.Single(x=>x.ClientSampleId==a.ClientSampleId).Value.Judgment);
    }
    [Fact] public async Task OP01_Sample_cap_counts_final_active_state_not_intermediate_order() {
        await using var f=await PersonalSessionFixture.CreateAsync();var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());
        b=b with {Operations=Enumerable.Range(0,50).Select(_=>s.Capture()).ToList()};var accepted=await s.Accept(b);
        Assert.Equal(50,accepted.Operations.Count);
        Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",s.Batch(s.Capture(version:1),1))).StatusCode);
        var old=accepted.Operations[0];var removal=s.Mutation(old,"RemoveSample",null);var capture=s.Capture(version:1);
        var result=await s.Accept(removal with {Operations=[capture,removal.Operations[0]]});
        Assert.Equal(2,result.BatchReceipt!.DraftVersion);Assert.Equal(50,(await s.Read())!.Samples.Count(x=>!x.IsRemoved));
        await s.AssertCounts(2,51,52,2);
    }
    [Fact] public async Task OP04_11_Body_limits_duplicate_fields_unknown_members_and_rate_limit() {
        await using var f=await PersonalSessionFixture.CreateAsync();var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());var url=s.Root+"/operation-batches";
        var serialized=JsonSerializer.Serialize(b,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var unknown=serialized.Insert(1,"\"accepted\":true,");
        Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsync(url,new StringContent(unknown,Encoding.UTF8,"application/json"))).StatusCode);
        var duplicate=serialized.Insert(1,"\"BATCHID\":\""+Guid.NewGuid()+"\",");
        Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsync(url,new StringContent(duplicate,Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await s.Client().PostAsync(url,new StringContent(new string('x',131073),Encoding.UTF8,"application/json"))).StatusCode);
        await s.AssertCounts(0,0,0,0);
        for(var i=0;i<27;i++) Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsJsonAsync(url,b with {CanonicalizationVersion=2})).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await s.Client().PostAsJsonAsync(url,b)).StatusCode);
    }
}
