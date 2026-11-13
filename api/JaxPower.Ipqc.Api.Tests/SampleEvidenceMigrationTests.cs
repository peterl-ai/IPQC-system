using System.Text.Json;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SampleEvidenceMigrationTests
{
    internal const string Previous = "20261113190630_R3205UnableToMeasureNgDraft";
    [Theory][InlineData(false)][InlineData(true)] public void Native_models_match_strictly_additive_DDL(bool postgres) {
        var b = new DbContextOptionsBuilder<IpqcDbContext>(); if (postgres) b.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else b.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(b.Options);
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var diff = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(diff.Count == 0, string.Join(";", diff.Select(d => d.GetType().Name + JsonSerializer.Serialize(d, d.GetType()))));
        var sql = db.GetService<IMigrator>().GenerateScript(Previous);
        foreach (var table in new[] { "SampleEvidenceOperations", "SampleEvidenceProjections" }) Assert.Contains("CREATE TABLE \"" + table + "\"", sql);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, "CREATE TABLE ").Count);
        Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.Contains("TR_binding_scope", sql); Assert.Contains("TR_binding_identity", sql);
        if (postgres) { Assert.Contains("\"BindingId\" uuid", sql); Assert.Contains("\"EnvironmentId\" character varying(200)", sql); Assert.Contains("\"DraftVersion\" bigint", sql); Assert.Contains("timestamp with time zone", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(), Previous));
    }
    internal static async Task<BindingSetup> Prior(PhotoFixture p) {
        var u = await SampleEvidenceTests.Setup(p); // All these producers already existed before this migration.
        var unable = new UnableSetup(u.S, u.Photo, u.Reference);
        var set = (await UnableToMeasureTests.Accept(unable, UnableToMeasureTests.Input(unable, 2))).Operation;
        await UnableToMeasureTests.Accept(unable, UnableToMeasureTests.Input(unable, 3, kind: "ClearUnableToMeasureNG", previous: set));
        return u;
    }
    internal static async Task<string> OldFacts(PhotoFixture p) => await DraftEvidenceAssessmentTests.Facts(p);
    [Fact] public async Task Populated_upgrade_preserves_every_old_fact_and_receipt() { await using var p = await PhotoFixture.Create(migration: Previous); await Upgrade(p); }
    internal static async Task Upgrade(PhotoFixture p) {
        var u = await Prior(p); var before = await OldFacts(p); var receipts = await p.F.Read(db => db.AcceptedBatchReceipts.OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync());
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await OldFacts(p));
        Assert.Equal(0, await p.F.Read(db => db.SampleEvidenceOperations.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.SampleEvidenceProjections.CountAsync()));
        await SampleEvidenceTests.Accept(u, SampleEvidenceTests.Input(u, 4));
        Assert.Equal(receipts, await p.F.Read(db => db.AcceptedBatchReceipts.Where(x => x.DraftVersion <= 4).OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync()));
        if (p.F.PostgresRoot is null) await p.F.Read(async db => { await db.Database.OpenConnectionAsync(); await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandText = "PRAGMA foreign_key_check"; await using var r = await q.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); return true; });
    }
    [Theory][InlineData("CREATE TABLE \"SampleEvidenceProjections\"")][InlineData("CREATE TRIGGER \"TR_binding_scope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Failed_upgrade_rolls_back_tables_guards_and_history(string point) { await using var p = await PhotoFixture.Create(migration: Previous); await Rollback(p, point); }
    internal static async Task Rollback(PhotoFixture p, string point) {
        _ = await Prior(p); var before = await OldFacts(p);
        p.F.Fault.Arm(point, true); await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync())); Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last())); Assert.Equal(before, await OldFacts(p));
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await OldFacts(p));
    }
    [Fact] public async Task Native_history_scope_projection_and_identity_guards() { await using var p = await PhotoFixture.Create(); await Integrity(p); }
    internal static async Task Integrity(PhotoFixture p) {
        var u = await SampleEvidenceTests.Setup(p); var first = (await SampleEvidenceTests.Accept(u, SampleEvidenceTests.Input(u))).Operation;
        async Task Native(Func<Task> command, string? code = null) {
            var error = await Record.ExceptionAsync(command); Assert.NotNull(error); var native = error.GetBaseException();
            if (p.F.PostgresRoot is not null) { Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(native).SqlState); if (code is not null) Assert.Equal(code, ((Npgsql.PostgresException)native).MessageText); }
            else { Assert.Contains(Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(native).SqliteExtendedErrorCode, new[] { 1811, 275 }); if (code is not null) Assert.Contains(code, native.Message); }
        }
        foreach (var sql in new[] { "UPDATE \"SampleEvidenceOperations\" SET \"TransitionKind\"='NGToOKCorrection'", "DELETE FROM \"SampleEvidenceOperations\"", "DELETE FROM \"SampleEvidenceProjections\"", "UPDATE \"SampleEvidenceProjections\" SET \"IsActive\"=false" })
            await Native(() => p.F.Edit(db => db.Database.ExecuteSqlRawAsync(sql)));
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(async db => (await db.SampleEvidenceOperations.SingleAsync()).ReceiptJson = "{}")); Assert.Contains("immutable", tracked.Message);
        var original = await p.F.Read(db => db.SampleEvidenceOperations.AsNoTracking().SingleAsync());
        // Reuse an accepted ID from a different domain with a perfectly real target/ref: native exclusion cannot rely on HTTP checks.
        var clone = JsonSerializer.Deserialize<SampleEvidenceOperation>(JsonSerializer.Serialize(original))!; clone.OperationId = u.Sample.OperationId; clone.BindingId = clone.OperationId;
        await Native(() => p.F.Edit(db => { db.SampleEvidenceOperations.Add(clone); return Task.CompletedTask; }), "OperationIdentityConflict");
        var other = (await u.S.Read())!.CurrentAttempts.Single(x => x.TaskItemId != u.S.ItemId);
        foreach (var mode in new[] { "scope", "classification", "target", "photo", "version" }) {
            clone = JsonSerializer.Deserialize<SampleEvidenceOperation>(JsonSerializer.Serialize(original))!; clone.OperationId = Guid.NewGuid(); clone.BindingId = clone.OperationId;
            if (mode == "scope") { clone.TaskItemId = other.TaskItemId; clone.InspectionAttemptId = other.InspectionAttemptId; }
            if (mode == "classification") clone.TransitionKind = "NGToOKCorrection";
            if (mode == "target") clone.SampleOperationId = Guid.NewGuid();
            if (mode == "photo") clone.PhotoReferenceOperationId = Guid.NewGuid();
            if (mode == "version") clone.DraftVersion = 4;
            await Native(() => p.F.Edit(db => { db.SampleEvidenceOperations.Add(clone); return Task.CompletedTask; }), "BindingScopeConflict");
            Assert.False(await p.F.Read(db => db.AcceptedOperationIdentities.AnyAsync(x => x.OperationId == clone.OperationId)));
        }
        var withdrawn = (await SampleEvidenceTests.Accept(u, SampleEvidenceTests.Input(u, 3, kind: "WithdrawSampleEvidence", previous: first))).Operation;
        await Native(() => p.F.Edit(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SampleEvidenceProjections\" SET \"LastOperationId\"={first.OperationId},\"IsActive\"=true")), "BindingProjectionConflict");
        Assert.Equal(withdrawn.OperationId, await p.F.Read(db => db.SampleEvidenceProjections.Select(x => x.LastOperationId).SingleAsync()));
    }
}
