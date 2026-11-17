using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoPolicyMigrationTests
{
    [Theory][InlineData(false)][InlineData(true)] public void Native_models_and_additive_four_table_contract(bool postgres) {
        var o = new DbContextOptionsBuilder<IpqcDbContext>(); if (postgres) o.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else o.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(o.Options); Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(PhotoPolicyFixture.Previous, "20261116145540_R3205ConfirmedPhotoPolicies");
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(sql, "CREATE TABLE ").Count); Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql);
        foreach (var t in new[] { "PhotoPolicyDefinitions", "PhotoPolicyConfirmations", "PhotoPolicyStates", "FrozenTaskPhotoPolicies" }) Assert.Contains("CREATE TABLE \"" + t + "\"", sql);
        Assert.Contains("TR_policy_confirmation_scope", sql); Assert.Contains("TR_policy_frozen_scope", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        foreach (var name in db.Model.GetEntityTypes().Where(x => x.GetTableName() is "PhotoPolicyDefinitions" or "PhotoPolicyConfirmations" or "PhotoPolicyStates" or "FrozenTaskPhotoPolicies")
            .SelectMany(x => x.GetForeignKeys()).Select(x => x.GetConstraintName())) Assert.True(name!.Length <= (postgres ? 63 : int.MaxValue), name);
        if (postgres) { Assert.Contains("\"DefinitionId\" uuid", sql); Assert.Contains("\"PolicyVersion\" bigint", sql); Assert.Contains("timestamp with time zone", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(), PhotoPolicyFixture.Previous));
    }
    [Fact] public async Task Populated_upgrade_preserves_every_prior_fact_and_never_backfills_confirmation() { await using var p = await PhotoFixture.Create(migration: PhotoPolicyFixture.Previous); await Upgrade(p); }
    internal static async Task<BindingSetup> Prior(PhotoFixture p) {
        var s = await SharedDraftFixture.Setup(p.F); var sample = await DraftEvidenceAssessmentTests.Sample(s); var (photo, reference) = await DraftEvidenceAssessmentTests.Photo(s);
        var u = new BindingSetup(s, sample, photo, reference!); await SampleEvidenceTests.Accept(u, SampleEvidenceTests.Input(u)); return u;
    }
    internal static async Task Upgrade(PhotoFixture p) {
        var u = await Prior(p); var before = await SampleEvidenceTests.Facts(p); var oldJson = await p.F.Read(db => db.PatrolTasks.Select(x => x.PlanningSnapshotJson).ToArrayAsync());
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        Assert.Equal(0, await p.F.Read(db => db.PhotoPolicyDefinitions.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.PhotoPolicyConfirmations.CountAsync()));
        Assert.Equal(0, await p.F.Read(db => db.PhotoPolicyStates.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.FrozenTaskPhotoPolicies.CountAsync()));
        var source = await p.F.Read(db => db.PatrolTasks.Select(x => x.PatrolStandardId).SingleAsync()); await PhotoPolicyFixture.ConfirmAll(p.F, source);
        Assert.Equal(oldJson, await p.F.Read(db => db.PatrolTasks.Select(x => x.PlanningSnapshotJson).ToArrayAsync()));
        Assert.False((await DraftEvidenceAssessmentTests.Item(u.S)).PhotoPolicy.HasExplicitConfirmation); Assert.Equal(1, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync()));
    }
    [Theory][InlineData("CREATE TABLE \"PhotoPolicyStates\"")][InlineData("CREATE TRIGGER \"TR_policy_confirmation_scope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Failed_migration_rolls_back_its_complete_contract(string point) { await using var p = await PhotoFixture.Create(migration: PhotoPolicyFixture.Previous); await Rollback(p, point); }
    internal static async Task Rollback(PhotoFixture p, string point) {
        _ = await Prior(p); var before = await SampleEvidenceTests.Facts(p); p.F.Fault.Arm(point, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync())); Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(PhotoPolicyFixture.Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last())); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await SampleEvidenceTests.Facts(p));
    }
    [Fact] public async Task Native_history_scope_and_forward_pointer_constraints_are_effective() { await using var f = await PersonalSessionFixture.CreateAsync(); await Integrity(f); }
    internal static async Task Integrity(PersonalSessionFixture f) {
        var s = await LineSharedPlanningFixture.Standard(f); var before = await PhotoPolicyTests.Facts(f);
        foreach (var sql in new[] { "UPDATE \"PhotoPolicyDefinitions\" SET \"DefinitionHash\"='rewrite'", "DELETE FROM \"PhotoPolicyDefinitions\"", "UPDATE \"PhotoPolicyConfirmations\" SET \"Value\"='Required'",
            "DELETE FROM \"PhotoPolicyConfirmations\"", "DELETE FROM \"PhotoPolicyStates\"", "UPDATE \"PhotoPolicyStates\" SET \"PolicyVersion\"=0" }) await Native(f, sql);
        Assert.Equal(before, await PhotoPolicyTests.Facts(f));
        var actual = await f.Read(db => db.PhotoPolicyConfirmations.AsNoTracking().OrderBy(x => x.StandardItemId).FirstAsync());
        foreach (var wrong in new[] { "actor", "definition" }) {
            var clone = JsonSerializer.Deserialize<JaxPower.Ipqc.Api.Domain.PhotoPolicyConfirmation>(JsonSerializer.Serialize(actual))!;
            clone.Id = Guid.NewGuid(); clone.RequestId = Guid.NewGuid(); clone.PolicyVersion++; clone.ExpectedConfirmationId = actual.Id;
            if (wrong == "actor") clone.ActorUserId = TestAccounts.AdminId;
            else clone.DefinitionId = await f.Read(db => db.PhotoPolicyDefinitions.Where(x => x.StandardItemId != actual.StandardItemId).Select(x => x.Id).FirstAsync());
            var rejected = await Record.ExceptionAsync(() => f.Edit(db => { db.PhotoPolicyConfirmations.Add(clone); return Task.CompletedTask; }));
            Assert.NotNull(rejected);
            if (f.PostgresRoot is not null) Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(rejected.GetBaseException()).SqlState);
            else Assert.Equal(1811, Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(rejected.GetBaseException()).SqliteExtendedErrorCode);
        }
        Assert.Equal(before, await PhotoPolicyTests.Facts(f));
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.PhotoPolicyConfirmations.FirstAsync()).ReceiptJson = "{}")); Assert.Contains("immutable", tracked.Message);
        var pqe = await f.Login("pqe"); var i = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var old = i.Confirmation!;
        var next = await PhotoPolicyFixture.Confirm(f, s, i.StandardItemId, PhotoPolicyFixture.Input(i, "Required"), pqe);
        var rewind = await Record.ExceptionAsync(() => f.Edit(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PhotoPolicyStates\" SET \"ConfirmationId\"={old.ConfirmationId},\"PolicyVersion\"={old.PolicyVersion} WHERE \"StandardItemId\"={i.StandardItemId}")));
        Assert.NotNull(rewind); Assert.Equal(next, (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0].Confirmation);
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero); await LineSharedPlanningFixture.Plan(f, s); f.Clock.Now = f.Clock.Now.AddMinutes(1); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(f));
        foreach (var sql in new[] { "UPDATE \"FrozenTaskPhotoPolicies\" SET \"SnapshotHash\"='rewrite'", "DELETE FROM \"FrozenTaskPhotoPolicies\"" }) await Native(f, sql);
        Assert.Equal(2, await f.Read(db => db.FrozenTaskPhotoPolicies.CountAsync()));
    }
    private static async Task Native(PersonalSessionFixture f, string sql) {
        var error = await Record.ExceptionAsync(() => f.Edit(db => db.Database.ExecuteSqlRawAsync(sql))); Assert.NotNull(error);
        if (f.PostgresRoot is not null) Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(error.GetBaseException()).SqlState);
        else Assert.Equal(1811, Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error.GetBaseException()).SqliteExtendedErrorCode);
    }
}
