using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftIntegrityTests
{
    internal static void Constraint(Exception ex,PersonalSessionFixture f,string message) {
        var cause=ex.GetBaseException();Assert.Contains(message,cause.Message,StringComparison.OrdinalIgnoreCase);
        if(f.PostgresRoot is null) Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(cause);else Assert.Equal("23514",Assert.IsType<Npgsql.PostgresException>(cause).SqlState);
    }
    [Fact] public async Task OP12_Ledger_receipts_audit_dependencies_and_mapping_are_immutable()
    {await using var f=await PersonalSessionFixture.CreateAsync();await Immutable(f);}
    internal static async Task Immutable(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var a=Assert.Single((await s.Accept(s.Batch(s.Capture()))).Operations);
        await s.Accept(s.Mutation(a,"CorrectSample",new("NG",null,null)));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Edit(async db=>(await db.ContributionOperations.FirstAsync()).ReceiptJson="{}"));
        Assert.Contains("immutable",error.Message);
        foreach(var (table,column) in new[]{("SharedSamples","SequenceNo"),("ContributionOperations","ReceiptJson"),("ContributionDependencies","ContentHash"),("AcceptedBatchReceipts","ReceiptJson"),("SharedAcceptanceAudits","AfterVersion")}) {
            foreach(var sql in new[]{$"UPDATE \"{table}\" SET \"{column}\"=\"{column}\"",$"DELETE FROM \"{table}\""}) {
                var failure=await Assert.ThrowsAnyAsync<Exception>(()=>f.Edit(async db=>await db.Database.ExecuteSqlRawAsync(sql)));
                Constraint(failure,f,"immutable");
            }
        }
        await s.AssertCounts(2,1,2,2);
        var restrict=await Assert.ThrowsAnyAsync<Exception>(()=>f.Edit(async db=>await db.PatrolTasks.Where(x=>x.Id==s.TaskId).ExecuteDeleteAsync()));
        Assert.Contains("FOREIGN KEY",restrict.GetBaseException().Message,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task OP01_12_Database_enforces_actual_Task_and_source_scope()
    {await using var f=await PersonalSessionFixture.CreateAsync();await Scope(f);}
    internal static async Task Scope(PersonalSessionFixture f) {
        var s=await SharedDraftFixture.Setup(f);var a=Assert.Single((await s.Accept(s.Batch(s.Capture()))).Operations);
        await LineSharedPlanningFixture.Plan(f,await LineSharedPlanningFixture.Standard(f),f.Line2);Assert.Equal(1,await LineSharedPlanningFixture.Generate(f));
        var other=(await LineSharedPlanningFixture.Tasks(f)).Single(x=>x.LineId==f.Line2);
        var identity=(await s.Client().GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(other.Id)))!.Items[0];
        var response=await s.Client(s.A).PostAsJsonAsync(WorkContextFixture.Root(other.Id),new WorkContextIssueInput(f.Line2,2,PersonalSessionFixture.Environment));
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);var source=(await response.Content.ReadFromJsonAsync<WorkContextIssued>())!;
        var otherSetup=new DraftSetup(f,other.Id,identity.TaskItemId,identity.InspectionAttemptId,s.A,s.B,source);
        var second=Assert.Single((await otherSetup.Accept(otherSetup.Batch(otherSetup.Capture()) with {ExpectedTaskLineId=f.Line2})).Operations);
        var sampleError=await Assert.ThrowsAsync<DbUpdateException>(()=>f.Edit(db=> {
            db.SharedSamples.Add(new() {Id=Guid.NewGuid(),TaskId=s.TaskId,TaskItemId=identity.TaskItemId,InspectionAttemptId=identity.InspectionAttemptId,
                ClientSampleId=Guid.NewGuid(),SequenceNo=99});return Task.CompletedTask;
        }));Constraint(sampleError,f,"scope mismatch");
        var original=await f.Read(db=>db.ContributionOperations.AsNoTracking().SingleAsync(x=>x.TaskId==s.TaskId));
        var bad=JsonSerializer.Deserialize<ContributionOperation>(JsonSerializer.Serialize(original))!;bad.OperationId=Guid.NewGuid();bad.SourceWorkContextId=source.Context.Id;
        var sourceError=await Assert.ThrowsAsync<DbUpdateException>(()=>f.Edit(db=> {db.ContributionOperations.Add(bad);return Task.CompletedTask;}));
        Constraint(sourceError,f,"scope mismatch");
        var scopeError=await Assert.ThrowsAnyAsync<Exception>(()=>f.Edit(async db=>await db.SharedSampleProjections.Where(x=>x.SampleId==a.SampleId)
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.LastOperationId,second.OperationId))));
        Assert.Contains("FOREIGN KEY",scopeError.GetBaseException().Message,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound,(await s.Client().GetAsync(s.Root+$"/operations/{second.OperationId}/receipt")).StatusCode);
        Assert.Equal(2,await f.Read(db=>db.ContributionOperations.CountAsync()));
    }
}
