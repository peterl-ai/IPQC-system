using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

// Real disposable PostgreSQL only; inherits the explicit opt-in/isolated-database safeguards.
public sealed class LineSharedPlanningPostgresTests
{
    [DisposablePostgresFact] public Task R03_30_33_Admission_Cas_and_workers() => LineSharedPlanningConcurrencyTests.AdmissionAndWorkers(true);
    [DisposablePostgresFact] public Task R03_19_20_Standard_gate() => LineSharedPlanningConcurrencyTests.StandardRace(true);
    [DisposablePostgresFact] public Task R03_21_23_Line_and_audit_lock_order() => LineSharedPlanningConcurrencyTests.LineFence(true);
    [DisposablePostgresFact] public Task R03_22_Account_security_serialization() => LineSharedPlanningConcurrencyTests.ActorRace(true);
    [DisposablePostgresFact] public Task R03_24_Atomic_snapshot_graph() => LineSharedPlanningConcurrencyTests.SnapshotRollback(true);
    [DisposablePostgresFact] public Task R03_17_18_Stale_discovery_and_disable() => LineSharedPlanningConcurrencyTests.StaleDiscovery(true);
    [DisposablePostgresFact] public Task R03_23_Same_actor_lock_graph() => LineSharedPlanningConcurrencyTests.SameActorLineAndPlan(true);
    [DisposablePostgresFact] public Task R03_19_24_Precommit_rollback() => LineSharedPlanningConcurrencyTests.CommitRollback(true);
    [DisposablePostgresFact]
    public async Task R03_31_33_Populated_previous_schema_upgrade_preserves_historical_duplicates_and_fks()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(true, migration: LineSharedPlanningMigrationTests.Previous);
        var standard = await LineSharedPlanningFixture.Standard(f); var now = f.Clock.Now.UtcDateTime;
        var plans = new[] { Guid.NewGuid(), Guid.NewGuid() }; var tasks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await f.Edit(async db =>
        {
            for (var i = 0; i < 2; i++)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "PatrolPlans" ("Id","WorkflowVersion","LineId","PlanNo","PlanName","PatrolStandardId","FactoryCode","FactoryName","LineCode","LineName","IsEnabled","EffectiveStartUtc","EffectiveEndUtc","ScheduleDefinition","TimeZoneId","GenerationNotBeforeUtc","CreatedBy","CreatedAtUtc","UpdatedBy","UpdatedAtUtc")
                    VALUES ({plans[i]},2,{f.Line1},{"SYN-" + i},{"Synthetic historical"},{standard},{""},{""},{""},{""},{false},{now},NULL,{""},{"America/New_York"},{now},{"synthetic"},{now},{"synthetic"},{now})
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "PatrolTasks" ("Id","WorkflowVersion","LineId","TaskNo","PatrolPlanId","PatrolStandardId","AssignedInspectorKey","ScheduledOccurrenceUtc","GeneratedAtUtc","GenerationSource","Status","CurrentRevisionNo","CreatedAtUtc","UpdatedAtUtc")
                    VALUES ({tasks[i]},2,{f.Line1},{"SYN-T-" + i},{plans[i]},{standard},{"historical"},{now},{now},{"Historical"},{"Completed"},0,{now},{now})
                    """);
            }
            await db.Database.MigrateAsync();
        });
        Assert.Equal(2, await f.Read(db => db.PatrolTasks.CountAsync()));
        Assert.All(await f.Read(db => db.PatrolTasks.ToListAsync()), x => { Assert.Null(x.PlanningSnapshotVersion); Assert.Null(x.PlanningSnapshotJson); Assert.Equal("historical", x.AssignedInspectorKey); Assert.Equal("Completed", x.Status); });
        Assert.All(await f.Read(db => db.PatrolPlans.ToListAsync()), x => Assert.Null(x.SharedPlanRevision));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => db.ProductionLines.Remove(await db.ProductionLines.SingleAsync(x => x.Id == f.Line1))));
        Assert.False(await f.Read(db => Task.FromResult(db.Database.HasPendingModelChanges())));
    }
    [DisposablePostgresFact]
    public async Task R03_22_Current_family_security_closures()
    {
        foreach (var closure in new[] { "Logout", "Role", "Deactivation" })
            await LineSharedPlanningConcurrencyTests.ActorRace(true, closure);
    }
    [DisposablePostgresFact]
    public Task Full_shared_standard_readiness_rejects_direct_and_http_bypasses_atomically() =>
        LineSharedStandardReadinessTests.InvalidUpdates(true, ["standard-name", "line-name", "1001-items", "missing-items", "null-items", "unknown-type", "invalid-limits", "sample-count", "null-item"]);
    [DisposablePostgresFact]
    public Task Shared_standard_identity_reconciliation_keeps_an_enabled_plan_ready() => LineSharedStandardReadinessTests.ValidUpdate(true);
}
