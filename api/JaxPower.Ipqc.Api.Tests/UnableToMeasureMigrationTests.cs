using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class UnableToMeasureMigrationTests
{
    internal const string Previous = "20261113172351_R3206AtomicDraftAttachmentReferences";
    [Theory][InlineData(false)][InlineData(true)] public void Both_models_and_strictly_additive_native_DDL(bool postgres) {
        var b = new DbContextOptionsBuilder<IpqcDbContext>(); if (postgres) b.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else b.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(b.Options);
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var diff = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(diff.Count == 0, string.Join(";", diff.Select(d => d.GetType().Name + JsonSerializer.Serialize(d, d.GetType()))));
        var sql = db.GetService<IMigrator>().GenerateScript(Previous);
        foreach (var table in new[] { "AcceptedOperationIdentities", "UnableToMeasureOperations", "UnableToMeasureProjections" }) Assert.Contains("CREATE TABLE \"" + table + "\"", sql);
        Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.Contains("TR_DraftOperationKeys_AllDomains", sql); Assert.Contains("TR_ContributionOperations_UnableGuard", sql); Assert.Contains("TR_Unable_Scope", sql);
        Assert.Contains("SELECT \"TaskId\",\"OperationId\",\"Domain\" FROM \"DraftOperationKeys\"", sql);
        if (postgres) { Assert.Contains("\"EnvironmentId\" character varying(200)", sql); Assert.Contains("\"NgReason\" character varying(2000)", sql); Assert.Contains("\"DraftVersion\" bigint", sql); Assert.Contains("timestamp with time zone", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(), Previous));
    }
    internal static async Task<(UnableSetup U, DraftBatchInput Old)> Prior(PhotoFixture p) {
        var (s, old, photo) = await DraftAttachmentMigrationTests.Prior(p);
        var reference = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, photo, 2))).Operations); return (new(s, photo, reference), old);
    }
    [Fact] public async Task Populated_upgrade_preserves_all_old_facts_and_receipts() { await using var p = await PhotoFixture.Create(migration: Previous); await Upgrade(p); }
    internal static async Task Upgrade(PhotoFixture p) {
        var (u, old) = await Prior(p); var before = await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true, true);
        var originals = await p.F.Read(db => db.AcceptedBatchReceipts.OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync());
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true, true));
        Assert.Equal(originals, await p.F.Read(db => db.AcceptedBatchReceipts.OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync()));
        Assert.Equal(4, await p.F.Read(db => db.AcceptedOperationIdentities.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.UnableToMeasureOperations.CountAsync()));
        Assert.True((await u.S.Accept(old with { SourceProofs = [] })).ReplayOnly); await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u, 3));
        Assert.Equal(originals, await p.F.Read(db => db.AcceptedBatchReceipts.Where(x => x.DraftVersion <= 3).OrderBy(x => x.BatchId).Select(x => x.ReceiptJson).ToArrayAsync()));
        if (p.F.PostgresRoot is null) await p.F.Read(async db => { await db.Database.OpenConnectionAsync(); await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandText = "PRAGMA foreign_key_check"; await using var r = await q.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); return true; });
    }
    [Theory][InlineData("CREATE TABLE \"UnableToMeasureProjections\"")][InlineData("INSERT INTO \"AcceptedOperationIdentities\"")]
    [InlineData("CREATE TRIGGER \"TR_Unable_Scope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Failed_migration_rolls_back_derived_keys_and_history(string point) { await using var p = await PhotoFixture.Create(migration: Previous); await Rollback(p, point); }
    internal static async Task Rollback(PhotoFixture p, string point) {
        _ = await Prior(p); var before = await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true, true);
        p.F.Fault.Arm(point, true); await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync())); Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true, true));
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true, true));
    }
    [Fact] public async Task Native_history_scope_projection_and_tracked_guards() { await using var p = await PhotoFixture.Create(); await Integrity(p); }
    internal static async Task Integrity(PhotoFixture p) {
        var u = await UnableToMeasureTests.Setup(p); var first = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u))).Operation;
        async Task Native(Func<Task> command, string? code = null) {
            var error = await Record.ExceptionAsync(command); Assert.NotNull(error); var native = error.GetBaseException();
            if (p.F.PostgresRoot is not null) { Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(native).SqlState); if (code is not null) Assert.Equal(code, ((Npgsql.PostgresException)native).MessageText); }
            else { Assert.Contains(Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(native).SqliteExtendedErrorCode, new[] { 1811, 275 }); if (code is not null) Assert.Contains(code, native.Message); }
        }
        foreach (var sql in new[] { "UPDATE \"UnableToMeasureOperations\" SET \"NgReason\"='rewrite'", "DELETE FROM \"UnableToMeasureOperations\"", "UPDATE \"AcceptedOperationIdentities\" SET \"Domain\"='other'",
            "DELETE FROM \"AcceptedOperationIdentities\"", "DELETE FROM \"UnableToMeasureProjections\"", "UPDATE \"UnableToMeasureProjections\" SET \"IsActive\"=false" })
            await Native(() => p.F.Edit(db => db.Database.ExecuteSqlRawAsync(sql)));
        var tracked = await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(async db => (await db.UnableToMeasureOperations.SingleAsync()).ReceiptJson = "{}")); Assert.Contains("immutable", tracked.Message);
        var clear = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u, 2, kind: "ClearUnableToMeasureNG", previous: first))).Operation;
        await Native(() => p.F.Edit(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UnableToMeasureProjections\" SET \"LastOperationId\"={first.OperationId},\"IsActive\"=true")), "UnableProjectionConflict");
        Assert.Equal(clear.OperationId, await p.F.Read(db => db.UnableToMeasureProjections.Select(x => x.LastOperationId).SingleAsync()));
        // A fresh actual other item/attempt pair and its accepted photo form the only inconsistent scope.
        var other = (await u.S.Read())!.CurrentAttempts.Single(x => x.TaskItemId != u.S.ItemId);
        var original = await p.F.Read(db => db.UnableToMeasureOperations.AsNoTracking().SingleAsync(x => x.OperationId == first.OperationId));
        var clone = JsonSerializer.Deserialize<UnableToMeasureOperation>(JsonSerializer.Serialize(original))!;
        clone.OperationId = Guid.NewGuid(); clone.TaskItemId = other.TaskItemId; clone.InspectionAttemptId = other.InspectionAttemptId;
        await Native(() => p.F.Edit(db => { db.UnableToMeasureOperations.Add(clone); return Task.CompletedTask; }), "UnableScopeConflict");
        Assert.False(await p.F.Read(db => db.AcceptedOperationIdentities.AnyAsync(x => x.OperationId == clone.OperationId)));
    }
    [Theory][InlineData("unable")][InlineData("sample")][InlineData("item")][InlineData("photo")]
    public async Task Four_domain_operation_identity_exclusion(string first) { await using var p = await PhotoFixture.Create(); await Identities(p, first); }
    internal static async Task Identities(PhotoFixture p, string first) {
        var u = await UnableToMeasureTests.Setup(p); var b = UnableToMeasureTests.Input(u); var id = b.Operation.OperationId; var s = u.S;
        var sample = s.Batch(s.Capture(version: 1) with { OperationId = id }, 1); var item = ItemRemovalTests.Input(s, 1); item = item with { Operation = item.Operation with { OperationId = id } };
        var bytes = PhotoFixture.Image(); var second = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); second = await PhotoFixture.Ready(s, second.AttachmentId, bytes);
        var photo = DraftAttachmentTests.Input(s, second, 1); photo = photo with { Operations = [photo.Operations[0] with { OperationId = id }] };
        if (first == "unable") {
            var r = (await UnableToMeasureTests.Accept(u, b)).Operation;
            await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u, 2, kind: "ClearUnableToMeasureNG", previous: r));
            sample = sample with { ExpectedDraftVersion = 3, Operations = [sample.Operations[0] with { BaseDraftVersion = 3 }] };
            item = item with { ExpectedDraftVersion = 3, Operation = item.Operation with { BaseDraftVersion = 3 } };
            photo = photo with { ExpectedDraftVersion = 3, Operations = [photo.Operations[0] with { BaseDraftVersion = 3 }] };
            foreach (var response in new[] { await s.Client().PostAsJsonAsync(s.Root + "/operation-batches", sample), await s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), item), await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), photo) }) {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
            }
        } else {
            if (first == "sample") await s.Accept(sample); else if (first == "item") await ItemRemovalTests.Accept(s, item); else await DraftAttachmentTests.Accept(s, photo);
            b = b with { ExpectedDraftVersion = 2, Operation = b.Operation with { BaseDraftVersion = 2 } };
            var response = await s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), b); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
            // An accepted ID in any domain remains a conflict even when the new envelope/version is unsupported.
            foreach (var changed in new[] { b with { CanonicalizationVersion = 2 }, b with { Operation = b.Operation with { CanonicalizationVersion = 2 } } })
                Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), changed)).StatusCode);
        }
        Assert.Equal(first == "unable" ? 3 : 2, (await s.Read())!.DraftVersion);
        Assert.Equal(await p.F.Read(db => db.DraftOperationKeys.CountAsync()) + await p.F.Read(db => db.UnableToMeasureOperations.CountAsync()), await p.F.Read(db => db.AcceptedOperationIdentities.CountAsync()));
    }
}
