using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftMigrationTests
{
    public const string Previous="20261112203300_R3205InitialInspectionAttemptIdentity";
    [Theory][InlineData(false)][InlineData(true)]
    public void OP12_Both_models_native_types_scope_and_immutable_DDL(bool postgres) {
        var options=new DbContextOptionsBuilder<IpqcDbContext>();
        if(postgres) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused");else options.UseSqlite("Data Source=:memory:");
        using var db=new IpqcDbContext(options.Options);Assert.False(db.Database.HasPendingModelChanges());
        var sql=db.GetService<IMigrator>().GenerateScript(Previous);
        foreach(var table in new[]{"SharedDrafts","SharedSamples","SharedSampleProjections","ContributionOperations","ContributionDependencies","AcceptedBatchReceipts","SharedAcceptanceAudits"}) Assert.Contains($"CREATE TABLE \"{table}\"",sql);
        Assert.DoesNotContain("ALTER TABLE",sql);Assert.DoesNotContain("DROP TABLE",sql);Assert.Contains("ON DELETE RESTRICT",sql);
        Assert.Contains("TR_SharedSamples_Scope",sql);Assert.Contains("TR_ContributionOperations_SourceScope",sql);Assert.Contains("Accepted history is immutable",sql);
        if(postgres) {Assert.Contains("\"SampleId\" uuid",sql);Assert.Contains("timestamp with time zone",sql);Assert.Contains("numeric",sql);}
        Assert.Throws<NotSupportedException>(()=>db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(),Previous));
    }
    [Fact] public async Task Populated_upgrade_preserves_every_old_fact_including_real_attempts()
    {await using var f=await PersonalSessionFixture.CreateAsync(migration:Previous);await Upgrade(f);}
    internal static async Task<Guid> OldBaseline(PersonalSessionFixture f) {
        var id=await InspectionIdentityTests.Existing(f);var auth=await f.Login();
        await f.Edit(async db=> {
            var items=await db.PatrolTaskItems.Where(x=>x.PatrolTaskId==id).ToListAsync();var creation=Guid.NewGuid();
            foreach(var item in items) {
                var attempt=new InspectionAttempt {Id=Guid.NewGuid(),PatrolTaskItemId=item.Id,CreationEventId=creation,CreationSource="TaskGeneration",
                    CreatedAtUtc=f.Clock.Now.UtcDateTime,FrozenSampleCount=item.SampleCount,MinimumSampleCount=1};
                db.InspectionAttempts.Add(attempt);db.InspectionAttemptApplicabilities.Add(new(){PatrolTaskItemId=item.Id,InspectionAttemptId=attempt.Id,InspectionAttempt=attempt});
            }
            var c=new WorkContext {Id=Guid.NewGuid(),ProofSecretHash=new string('0',64),UserId=TestAccounts.Ipqa1Id,TaskId=id,LineId=f.Line1,
                EnvironmentId=PersonalSessionFixture.Environment,IssuedPersonalSessionId=auth.PersonalSessionId!.Value,IssuedAuthVersion=1,
                IssuedAtUtc=f.Clock.Now.UtcDateTime,CaptureUntilUtc=f.Clock.Now.AddHours(72).UtcDateTime,PersonalSessionExpiresAtUtc=f.Clock.Now.AddDays(14).UtcDateTime};
            db.WorkContexts.Add(c);new WorkContextSecurity(db,f.Clock).Audit(c,TestAccounts.Ipqa1Id,"dev-ipqa-1","ContextIssued");
        });
        var legacy=await f.Task(workflow:WorkflowVersion.LegacyPersonalV1);
        await f.Edit(async db=>(await db.PatrolTaskItems.FirstAsync(x=>x.PatrolTaskId==legacy)).Remarks="Synthetic retained answer");
        return id;
    }
    internal static async Task Upgrade(PersonalSessionFixture f) {
        var id=await OldBaseline(f);var before=await InspectionIdentityMigrationTests.OldFacts(f,true);
        await f.Edit(async db=>await db.Database.MigrateAsync());
        Assert.Equal(before,await InspectionIdentityMigrationTests.OldFacts(f,true));
        Assert.Equal(0,await f.Read(db=>db.SharedDrafts.CountAsync()));Assert.Equal(0,await f.Read(db=>db.ContributionOperations.CountAsync()));
        Assert.Equal(0,await f.Read(db=>db.SharedSamples.CountAsync()));Assert.Equal(2,await f.Read(db=>db.InspectionAttempts.CountAsync()));
        if(f.PostgresRoot is null) await f.Read(async db=> {
            await db.Database.OpenConnectionAsync();await using var command=db.Database.GetDbConnection().CreateCommand();command.CommandText="PRAGMA foreign_key_check";
            await using var reader=await command.ExecuteReaderAsync();Assert.False(await reader.ReadAsync());return true;
        });
        var user=await f.Login();var source=await WorkContextFixture.Issue(f,id,user);
        var item=await f.Read(db=>db.InspectionAttempts.AsNoTracking().FirstAsync());
        var s=new DraftSetup(f,id,item.PatrolTaskItemId,item.Id,user,user,source);
        var outcome=await s.Accept(s.Batch(s.Capture()));Assert.Single(outcome.Operations);Assert.Equal(1,(await s.Read())!.DraftVersion);
    }
    [Theory][InlineData("CREATE TABLE \"SharedSamples\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Upgrade_fault_rolls_back_schema_and_preserves_history(string point)
    {await using var f=await PersonalSessionFixture.CreateAsync(migration:Previous);await UpgradeRollback(f,point);}
    internal static async Task UpgradeRollback(PersonalSessionFixture f,string point) {
        await OldBaseline(f);var before=await InspectionIdentityMigrationTests.OldFacts(f,true);f.Fault.Arm(point,true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Edit(async db=>await db.Database.MigrateAsync()));
        Assert.Equal(before,await InspectionIdentityMigrationTests.OldFacts(f,true));
        await f.Edit(async db=>await db.Database.MigrateAsync());Assert.Equal(before,await InspectionIdentityMigrationTests.OldFacts(f,true));
        Assert.Equal(0,await f.Read(db=>db.ContributionOperations.CountAsync()));
    }
}
