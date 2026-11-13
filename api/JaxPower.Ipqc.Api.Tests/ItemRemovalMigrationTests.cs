using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ItemRemovalMigrationTests
{
    internal const string Previous = "20261112212127_R3204TypedSharedDraftAcceptance";
    [Theory][InlineData(false)][InlineData(true)]
    public void RM15_Provider_models_and_additive_native_DDL(bool postgres) {
        var options = new DbContextOptionsBuilder<IpqcDbContext>();
        if (postgres) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options); Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(Previous);
        Assert.Contains("CREATE TABLE \"ItemRemovalOperations\"", sql); Assert.Contains("CREATE TABLE \"SharedOperationIdentities\"", sql);
        Assert.DoesNotContain("CREATE TABLE \"ContributionOperations\"", sql); Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("ON DELETE RESTRICT", sql); Assert.Contains("SELECT \"TaskId\",\"OperationId\",'Sample' FROM \"ContributionOperations\"", sql);
        Assert.Contains("TR_ContributionOperations_Identity", sql); Assert.Contains("TR_ItemRemovalOperations_ScopeIdentity", sql);
        if (postgres) { Assert.Contains("\"OperationId\" uuid", sql); Assert.Contains("timestamp with time zone", sql); Assert.Contains("\"EnvironmentId\" character varying(200)", sql); }
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(), Previous));
    }

    // Synthetic old persistence fixture; actual generated task/item/attempt and issued A/B identities.
    // Do not run the new service against a schema that has not yet been upgraded.
    internal static async Task<(DraftSetup Setup, DraftBatchInput Batch)> PriorAccepted(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var capture = s.Capture(); var b = s.Batch(capture);
        var captureHash = DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId, b, capture));
        var correction = capture with { OperationId = Guid.NewGuid(), Kind = "CorrectSample", Data = new("NG", null, null), Dependencies = [new(capture.OperationId, captureHash)] };
        b = b with { Operations = [capture, correction] };
        var sampleId = Guid.NewGuid(); var now = f.Clock.Now.UtcDateTime;
        DraftOperationReceipt Receipt(DraftOperationInput o, string hash) => new(s.TaskId, o.OperationId, b.BatchId, 1, hash, o.Kind, s.ItemId, s.AttemptId,
            sampleId, capture.ClientSampleId, s.Source.Context.Id, s.A.User.Id, s.B.User.Id, s.B.PersonalSessionId!.Value,
            PersonalSessionFixture.Environment, f.Line1, 2, now, 1, new("Qualitative", null, o.Data!.Judgment!, null), false, o.OperationId);
        var hashes = b.Operations.ToDictionary(o => o.OperationId, o => DraftCanonical.Hash(DraftCanonical.Operation(s.TaskId, b, o)));
        var receipts = b.Operations.Select(o => Receipt(o, hashes[o.OperationId])).OrderBy(x => DraftCanonical.Key(x.OperationId), StringComparer.Ordinal).ToList();
        var fingerprint = DraftCanonical.Batch(s.TaskId, b, hashes);
        var batch = new DraftBatchReceipt(s.TaskId, b.BatchId, fingerprint, s.B.User.Id, s.B.PersonalSessionId!.Value, now, 0, 1, receipts);
        await f.Edit(async db => {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.SharedDrafts.Add(new() { TaskId = s.TaskId, DraftVersion = 1 }); await db.SaveChangesAsync();
            db.SharedSamples.Add(new() { Id = sampleId, TaskId = s.TaskId, TaskItemId = s.ItemId, InspectionAttemptId = s.AttemptId, ClientSampleId = capture.ClientSampleId, SequenceNo = 1 });
            await db.SaveChangesAsync();
            db.AcceptedBatchReceipts.Add(new() { TaskId = s.TaskId, BatchId = b.BatchId, DraftVersion = 1, RequestFingerprint = fingerprint, ReceiptJson = JsonSerializer.Serialize(batch, DraftCanonical.Json) });
            await db.SaveChangesAsync();
            foreach (var o in b.Operations) db.ContributionOperations.Add(new() { TaskId = s.TaskId, OperationId = o.OperationId, BatchId = b.BatchId,
                TaskItemId = s.ItemId, InspectionAttemptId = s.AttemptId, SampleId = sampleId, SourceWorkContextId = s.Source.Context.Id,
                ContributorUserId = s.A.User.Id, UploadedByUserId = s.B.User.Id, UploadedPersonalSessionId = s.B.PersonalSessionId.Value,
                EnvironmentId = PersonalSessionFixture.Environment, LineId = f.Line1, Kind = o.Kind, ContentHash = hashes[o.OperationId],
                CanonicalPayload = DraftCanonical.Operation(s.TaskId, b, o), ReceiptJson = JsonSerializer.Serialize(Receipt(o, hashes[o.OperationId]), DraftCanonical.Json), AcceptedAtUtc = now, DraftVersion = 1 });
            await db.SaveChangesAsync();
            db.ContributionDependencies.Add(new() { TaskId = s.TaskId, OperationId = correction.OperationId, DependencyId = capture.OperationId, ContentHash = captureHash });
            db.SharedSampleProjections.Add(new() { SampleId = sampleId, TaskId = s.TaskId, LastOperationId = correction.OperationId,
                ValueOperationId = correction.OperationId, InspectionType = "Qualitative", Judgment = "NG" });
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = s.TaskId, BatchId = b.BatchId, ActorUserId = s.B.User.Id,
                PersonalSessionId = s.B.PersonalSessionId.Value, OccurredAtUtc = now, BeforeVersion = 0, AfterVersion = 1, NewOperationCount = 2 });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        });
        return (s, b);
    }

    [Fact] public async Task RM15_Populated_upgrade_keeps_every_old_fact_and_V1_recovery()
    { await using var f = await PersonalSessionFixture.CreateAsync(migration: Previous); await Upgrade(f); }
    internal static async Task Upgrade(PersonalSessionFixture f) {
        var (s, b) = await PriorAccepted(f); var before = await InspectionIdentityMigrationTests.OldFacts(f, true, true);
        var history = await ItemRemovalTests.Retained(f);
        await f.Edit(async db => await db.Database.MigrateAsync());
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(f, true, true));
        Assert.Equal(0, await f.Read(db => db.ItemRemovalOperations.CountAsync()));
        Assert.Equal(2, await f.Read(db => db.SharedOperationIdentities.CountAsync(x => x.Domain == "Sample")));
        var replay = await s.Accept(b with { SourceProofs = [] }); Assert.True(replay.ReplayOnly); Assert.Equal(2, replay.Operations.Count);
        var removed = await ItemRemovalTests.Accept(s, ItemRemovalTests.Input(s, 1)); Assert.Equal("Removed", removed.Operation.Disposition);
        Assert.Equal(history, await ItemRemovalTests.Retained(f));
        await ItemRemovalTests.Counts(s, 2, 1, 1, 2, 2);
        if (f.PostgresRoot is null) await f.Read(async db => {
            await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await command.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync()); return true;
        });
    }

    [Theory][InlineData("CREATE TABLE \"ItemRemovalOperations\"")][InlineData("INSERT INTO \"SharedOperationIdentities\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task RM15_Additive_failure_rolls_back_schema_index_and_trigger(string point)
    { await using var f = await PersonalSessionFixture.CreateAsync(migration: Previous); await Rollback(f, point); }
    internal static async Task Rollback(PersonalSessionFixture f, string point) {
        await PriorAccepted(f); var before = await InspectionIdentityMigrationTests.OldFacts(f, true, true); f.Fault.Arm(point, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => await db.Database.MigrateAsync()));
        Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(f, true, true));
        Assert.Equal(Previous, (await f.Read(async db => (await db.Database.GetAppliedMigrationsAsync()).Last())));
        await f.Edit(async db => await db.Database.MigrateAsync());
        Assert.Equal(before, await InspectionIdentityMigrationTests.OldFacts(f, true, true));
        Assert.Equal(2, await f.Read(db => db.SharedOperationIdentities.CountAsync()));
    }
}
