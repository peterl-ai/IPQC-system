using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftAttachmentMigrationTests
{
    internal const string Previous = "20261113153316_R3206ProtectedPhotoReady";
    [Theory][InlineData(false)][InlineData(true)] public void Both_provider_models_and_additive_DDL(bool postgres) {
        var b = new DbContextOptionsBuilder<IpqcDbContext>(); if (postgres) b.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else b.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(b.Options);
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var diffs = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(diffs.Count == 0, string.Join(";", diffs.Select(d => d.GetType().Name + ":" + JsonSerializer.Serialize(d, d.GetType()))));
        var sql = db.GetService<IMigrator>().GenerateScript(Previous);
        foreach (var table in new[] { "DraftOperationKeys", "DraftAttachmentOperations", "DraftAttachmentProjections" }) Assert.Contains("CREATE TABLE \"" + table + "\"", sql);
        Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("CREATE TABLE \"SharedOperationIdentities\"", sql);
        Assert.Contains("TR_SharedOperationIdentities_AllDomains", sql); Assert.Contains("TR_DraftPhoto_Scope", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.Contains("SELECT \"TaskId\",\"OperationId\",\"Domain\" FROM \"SharedOperationIdentities\"", sql);
        if (postgres) { Assert.Contains("\"EnvironmentId\" character varying(200)", sql); Assert.Contains("\"DraftVersion\" bigint", sql); Assert.Contains("timestamp with time zone", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(), Previous));
    }
    internal static async Task<(DraftSetup S, DraftBatchInput Old, AttachmentOutput Photo)> Prior(PhotoFixture p) {
        var (s, old) = await ItemRemovalMigrationTests.PriorAccepted(p.F);
        var bytes = PhotoFixture.Image(); var a = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); a = await PhotoFixture.Ready(s, a.AttachmentId, bytes);
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 1, other)); return (s, old, a);
    }
    [Fact] public async Task Populated_upgrade_preserves_all_rows_and_exact_receipts() { await using var p = await PhotoFixture.Create(migration: Previous); await Upgrade(p); }
    internal static async Task Upgrade(PhotoFixture p) {
        var (s, old, a) = await Prior(p); var before = await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true);
        var json = await p.F.Read(db => db.AcceptedBatchReceipts.Where(x => x.BatchId == old.BatchId).Select(x => x.ReceiptJson).SingleAsync());
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true));
        Assert.Equal(3, await p.F.Read(db => db.DraftOperationKeys.CountAsync())); Assert.Equal(0, await p.F.Read(db => db.DraftAttachmentOperations.CountAsync()));
        Assert.Equal(json, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{old.BatchId}/receipt")); Assert.True((await s.Accept(old with { SourceProofs = [] })).ReplayOnly);
        await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, 2));
        Assert.Equal(json, await s.Client().GetStringAsync(s.Root + $"/operation-batches/{old.BatchId}/receipt"));
        if (p.F.PostgresRoot is null) await p.F.Read(async db => { await db.Database.OpenConnectionAsync(); await using var q = db.Database.GetDbConnection().CreateCommand(); q.CommandText = "PRAGMA foreign_key_check"; await using var r = await q.ExecuteReaderAsync(); Assert.False(await r.ReadAsync()); return true; });
    }
    [Theory][InlineData("CREATE TABLE \"DraftAttachmentProjections\"")][InlineData("INSERT INTO \"DraftOperationKeys\"")]
    [InlineData("CREATE TRIGGER \"TR_DraftPhoto_Scope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Migration_failure_preserves_old_ready_and_receipts(string point) { await using var p = await PhotoFixture.Create(migration: Previous); await Rollback(p, point); }
    internal static async Task Rollback(PhotoFixture p, string point) {
        _ = await Prior(p); var before = await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true);
        p.F.Fault.Arm(point, true); await Assert.ThrowsAsync<InvalidOperationException>(() => p.F.Edit(db => db.Database.MigrateAsync())); Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(Previous, await p.F.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last()));
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true));
        await p.F.Edit(db => db.Database.MigrateAsync()); Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(p.F, true, true, true, true));
    }
    [Fact] public async Task Native_history_projection_and_scope_guards() { await using var p = await PhotoFixture.Create(); await Integrity(p); }
    internal static async Task Integrity(PhotoFixture p) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); var r = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a))).Operations);
        async Task Reject(string sql) {
            var error = await Record.ExceptionAsync(() => p.F.Edit(db => db.Database.ExecuteSqlRawAsync(sql))); Assert.NotNull(error);
            if (p.F.PostgresRoot is not null) Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(error).SqlState);
            else Assert.Contains(Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error).SqliteExtendedErrorCode, new[] { 1811, 275 });
        }
        foreach (var sql in new[] { "UPDATE \"DraftAttachmentOperations\" SET \"Kind\"='DetachAttachment'", "DELETE FROM \"DraftAttachmentOperations\"",
            "UPDATE \"DraftOperationKeys\" SET \"Domain\"='Sample'", "DELETE FROM \"DraftOperationKeys\"", "DELETE FROM \"DraftAttachmentProjections\"",
            "UPDATE \"DraftAttachmentProjections\" SET \"IsAttached\"=false" }) await Reject(sql);
        // Every parent exists, bytes really are Ready, and the predecessor is unused.
        // Only the photo/item/attempt or frozen provenance combination is inconsistent.
        var bytes = PhotoFixture.Image(); var second = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); second = await PhotoFixture.Ready(s, second.AttachmentId, bytes);
        var other = (await s.Read())!.CurrentAttempts.Single(x => x.TaskItemId != s.ItemId);
        var row = await p.F.Read(db => db.DraftAttachmentOperations.AsNoTracking().SingleAsync());
        foreach (var mismatch in new[] { "scope", "photo-owner" }) {
            var clone = JsonSerializer.Deserialize<JaxPower.Ipqc.Api.Domain.DraftAttachmentOperation>(JsonSerializer.Serialize(row))!;
            clone.OperationId = Guid.NewGuid(); clone.AttachmentId = second.AttachmentId; clone.ClientPhotoId = second.ClientPhotoId;
            if (mismatch == "scope") { clone.TaskItemId = other.TaskItemId; clone.InspectionAttemptId = other.InspectionAttemptId; }
            else clone.PhotoContributorUserId = s.B.User.Id;
            var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => p.F.Edit(db => { db.DraftAttachmentOperations.Add(clone); return Task.CompletedTask; }));
            var native = invalid.GetBaseException();
            if (p.F.PostgresRoot is not null) { Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(native).SqlState); Assert.Equal("DraftAttachmentScopeConflict", ((Npgsql.PostgresException)native).MessageText); }
            else { Assert.Equal(1811, Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(native).SqliteExtendedErrorCode); Assert.Contains("DraftAttachmentScopeConflict", native.Message); }
            Assert.False(await p.F.Read(db => db.DraftOperationKeys.AnyAsync(x => x.OperationId == clone.OperationId)));
        }
        var detached = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, a, 1, previous: r, kind: "DetachAttachment"))).Operations);
        // A former valid FK target cannot rewind the projection to accepted older content.
        var old = r.OperationId;
        var failure = await Record.ExceptionAsync(() => p.F.Edit(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DraftAttachmentProjections\" SET \"LastOperationId\"={old},\"IsAttached\"=true")));
        if (p.F.PostgresRoot is not null) Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(failure).SqlState);
        else Assert.Equal(1811, Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(failure).SqliteExtendedErrorCode);
        Assert.Equal(detached.OperationId, await p.F.Read(db => db.DraftAttachmentProjections.Select(x => x.LastOperationId).SingleAsync()));
    }
    [Theory][InlineData("photo")][InlineData("sample")][InlineData("item")]
    public async Task Cross_domain_ids_cannot_be_reused(string first) { await using var p = await PhotoFixture.Create(); await Identities(p, first); }
    internal static async Task Identities(PhotoFixture p, string first) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); var b = DraftAttachmentTests.Input(s, a); var id = b.Operations[0].OperationId;
        var sample = s.Batch(s.Capture() with { OperationId = id }); var item = ItemRemovalTests.Input(s); item = item with { Operation = item.Operation with { OperationId = id } };
        if (first == "photo") {
            await DraftAttachmentTests.Accept(s, b);
            sample = sample with { ExpectedDraftVersion = 1, Operations = [sample.Operations[0] with { BaseDraftVersion = 1 }] };
            item = item with { ExpectedDraftVersion = 1, Operation = item.Operation with { BaseDraftVersion = 1 } };
            foreach (var response in new[] { await s.Client().PostAsJsonAsync(s.Root + "/operation-batches", sample), await s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), item) }) {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
            }
        } else {
            if (first == "sample") await s.Accept(sample); else await ItemRemovalTests.Accept(s, item);
            b = b with { ExpectedDraftVersion = 1, Operations = [b.Operations[0] with { BaseDraftVersion = 1 }] };
            var response = await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("OperationIdentityConflict", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, (await s.Read())!.DraftVersion); Assert.Equal(1, await p.F.Read(db => db.DraftOperationKeys.CountAsync()));
        Assert.Equal(1, await p.F.Read(db => db.AcceptedBatchReceipts.CountAsync()));
    }
}
