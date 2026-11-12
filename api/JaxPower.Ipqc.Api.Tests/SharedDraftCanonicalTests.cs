using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftCanonicalTests
{
    [Fact] public void OP11_Frozen_V1_byte_vector_decimal_null_and_Unicode() {
        Guid Id(int i)=>Guid.Parse("00000000-0000-0000-0000-"+i.ToString("D12"));
        var op=new DraftOperationInput(Id(3),1,"CaptureSample",Id(4),Id(5),Id(6),Id(7),Id(8),0,1,new(null,1.2300m,"汉字"),[]);
        var b=new DraftBatchInput(Id(9),1,Id(2),2,"synthetic",0,[op],[],[new(Id(7),"not-in-business-hash")]);
        const string expected="""{"v":1,"environment":"synthetic","task":"00000000-0000-0000-0000-000000000001","line":"00000000-0000-0000-0000-000000000002","workflow":2,"operation":"00000000-0000-0000-0000-000000000003","kind":"CaptureSample","item":"00000000-0000-0000-0000-000000000004","attempt":"00000000-0000-0000-0000-000000000005","clientSample":"00000000-0000-0000-0000-000000000006","source":"00000000-0000-0000-0000-000000000007","contributorAssertion":"00000000-0000-0000-0000-000000000008","base":0,"local":1,"data":{"judgment":null,"value":"1.23","barcode":"\u6C49\u5B57"},"dependencies":[]}""";
        var actual=DraftCanonical.Operation(Id(1),b,op);Assert.Equal(expected,actual);
        Assert.Equal(actual,DraftCanonical.Operation(Id(1),b,op with {Data=op.Data! with {InspectionValue=1.23m}}));
        Assert.NotEqual(actual,DraftCanonical.Operation(Id(1),b,op with {Data=op.Data! with {Barcode=""}}));
        Assert.NotEqual(DraftCanonical.Operation(Id(1),b,op with {Data=new(null,0m,"é")}),DraftCanonical.Operation(Id(1),b,op with {Data=new(null,0m,"e\u0301")}));
        var hashes=new Dictionary<Guid,string>{{op.OperationId,DraftCanonical.Hash(actual)}};
        Assert.Equal(DraftCanonical.Batch(Id(1),b,hashes),DraftCanonical.Batch(Id(1),b with {SourceProofs=[]},hashes));
        Assert.DoesNotContain("not-in-business-hash",actual);
        Assert.NotEqual(DraftCanonical.Operation(Id(1),b,op with {Data=new(null,0m,null)}),DraftCanonical.Operation(Id(1),b,op with {Data=new(null,0m,"")}));
        Assert.Equal(DraftCanonical.Operation(Id(1),b,op with {Data=new(null,0m,null)}),DraftCanonical.Operation(Id(1),b,op with {Data=new(null,-0.000m,null)}));
        var references=new List<DraftOperationReference>{new(Id(12),new string('A',64)),new(Id(11),new string('B',64))};
        Assert.Equal(DraftCanonical.Operation(Id(1),b,op with {Dependencies=references}),DraftCanonical.Operation(Id(1),b,op with {Dependencies=references.AsEnumerable().Reverse().ToList()}));
        var multiple=new Dictionary<Guid,string>{{Id(12),new string('A',64)},{Id(11),new string('B',64)}};
        Assert.Equal(DraftCanonical.Batch(Id(1),b with {AcceptedReferences=references},multiple),
            DraftCanonical.Batch(Id(1),b with {AcceptedReferences=references.AsEnumerable().Reverse().ToList()},multiple.Reverse().ToDictionary()));
    }
    [Fact] public async Task OP11_No_source_secret_or_credentials_in_business_storage_or_responses()
    {await using var f=await PersonalSessionFixture.CreateAsync();await Redaction(f);}
    internal static async Task Redaction(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var b=s.Batch(s.Capture());var accepted=await s.Accept(b);
        var business=await f.Read(async db=>JsonSerializer.Serialize(new {
            Operations=await db.ContributionOperations.AsNoTracking().ToListAsync(),
            Batches=await db.AcceptedBatchReceipts.AsNoTracking().ToListAsync(),Audit=await db.SharedAcceptanceAudits.AsNoTracking().ToListAsync()}));
        business+=JsonSerializer.Serialize(accepted)+JsonSerializer.Serialize(await s.Read());
        foreach(var secret in new[]{s.Source.ProofSecret,DraftCanonical.Hash(s.Source.ProofSecret),s.A.AccessToken,s.B.AccessToken,s.A.RefreshToken!,s.B.RefreshToken!}) Assert.DoesNotContain(secret,business);
        var canonical=await f.Read(db=>db.ContributionOperations.Select(x=>x.CanonicalPayload).SingleAsync());
        Assert.DoesNotContain(s.B.PersonalSessionId!.Value.ToString("D"),canonical);
        Assert.DoesNotContain("ProofSecret",business,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b with {CanonicalizationVersion=2})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsJsonAsync(s.Root+"/operation-batches",b with {BatchId=Guid.NewGuid(),Operations=[],SourceProofs=[],CanonicalizationVersion=2,
            AcceptedReferences=[new(accepted.Operations[0].OperationId,accepted.Operations[0].ContentHash)]})).StatusCode);
    }
    [Fact] public async Task OP11_Quantitative_JSON_strings_are_not_silently_coerced() {
        await using var f=await PersonalSessionFixture.CreateAsync();var s=await SharedDraftFixture.Setup(f,true);var b=s.Batch(s.Capture(new(null,2,null)));
        var json=JsonSerializer.Serialize(b,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var coerced=json.Replace("\"inspectionValue\":2","\"inspectionValue\":\"2\"",StringComparison.Ordinal);Assert.NotEqual(json,coerced);
        Assert.Equal(HttpStatusCode.BadRequest,(await s.Client().PostAsync(s.Root+"/operation-batches",new StringContent(coerced,Encoding.UTF8,"application/json"))).StatusCode);
        await s.AssertCounts(0,0,0,0);
    }
}
