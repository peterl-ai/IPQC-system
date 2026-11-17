using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    private static bool ValidHeaderValue(string? value) {
        if (value is null) return true;
        if (value.Length > 400) return false;
        var count = 0;
        for (var i = 0; i < value.Length;) {
            if (!Rune.TryGetRuneAt(value, i, out var rune) || Rune.IsControl(rune) || ++count > 200) return false;
            i += rune.Utf16SequenceLength;
        }
        return true;
    }
    private static BasicInformationReceipt HeaderReceipt(TaskHeaderOperation row) {
        try {
            if (!Enum.TryParse<BasicInformationField>(row.Field, out var field) || !Enum.IsDefined(field)) throw new JsonException();
            var previous = row.PredecessorOperationId is { } id ? new DraftOperationReference(id, row.PredecessorHash!) : null;
            var expected = new BasicInformationReceipt(row.TaskId, row.OperationId, row.BatchId, row.CanonicalizationVersion,
                row.ContentHash, row.Kind, field, row.Value, row.SourceWorkContextId, row.ContributorUserId, row.ActorUserId,
                row.ActorPersonalSessionId, row.EnvironmentId, row.LineId, row.WorkflowVersion, row.BaseDraftVersion,
                row.OriginLocalEditVersion, DateTime.SpecifyKind(row.AcceptedAtUtc, DateTimeKind.Utc), row.DraftVersion, previous);
            var op = new BasicInformationOperationInput(row.OperationId, row.CanonicalizationVersion, row.Kind, field,
                row.SourceWorkContextId, row.ContributorUserId, row.BaseDraftVersion, row.OriginLocalEditVersion, row.Value, previous);
            var input = new BasicInformationBatchInput(row.BatchId, 1, row.LineId, row.WorkflowVersion, row.EnvironmentId, row.BaseDraftVersion, op, null);
            var receipt = JsonSerializer.Deserialize<BasicInformationReceipt>(row.ReceiptJson, DraftCanonical.Json);
            if (receipt != expected || Encode(receipt) != row.ReceiptJson || BasicInformationCanonical.Operation(row.TaskId, input) != row.CanonicalPayload ||
                DraftCanonical.Hash(row.CanonicalPayload) != row.ContentHash) throw new JsonException();
            return receipt;
        } catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException) {
            throw Error("BasicInformationReceiptIntegrityUnavailable", 503);
        }
    }
    private static BasicInformationReceipt ScopedHeaderReceipt(TaskHeaderOperation row, PatrolTask task, string environment) {
        if (row.TaskId != task.Id || row.LineId != task.LineId || row.WorkflowVersion != 2 || row.EnvironmentId != environment)
            throw Error("BasicInformationReceiptIntegrityUnavailable", 503);
        return HeaderReceipt(row);
    }
    public Task<BasicInformationBatchResult> AcceptBasicInformationAsync(Guid taskId, BasicInformationBatchInput b, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true) throw Error("PersonalSessionRequired", 401);
        // Entire manifest is immutable records/scalars, including predecessor and proof transport.
        var sources = b?.Operation is { } input ? new[] { input.SourceWorkContextId } : [];
        return IdentityBoundary(() => store.RunAsync(actor, false, taskId, sources, async authority => {
            if (b is null || b.BatchId == Guid.Empty || b.ExpectedTaskLineId == Guid.Empty || b.ExpectedDraftVersion < 0 ||
                b.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } || b.Operation is not { } o || o.OperationId == Guid.Empty ||
                !Enum.IsDefined(o.Field) || o.SourceWorkContextId == Guid.Empty || o.ContributorUserIdAssertion == Guid.Empty ||
                o.BaseDraftVersion < 0 || o.OriginLocalEditVersion < 0 || !ValidHeaderValue(o.Value) ||
                o.Predecessor is { } predecessor && (predecessor.OperationId == Guid.Empty || !DraftCanonical.IsHash(predecessor.ContentHash)) ||
                b.SourceProof is { } proof && proof.WorkContextId != o.SourceWorkContextId) throw Error("InvalidBasicInformationBatch", 400);
            var task = await identity.Target(taskId, b.ExpectedTaskLineId, ct);
            if (b.ExpectedWorkflowVersion != 2 || b.ExpectedEnvironmentId != authority.EnvironmentId) throw Error("DraftScopeMismatch");
            var batch = await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == b.BatchId, ct);
            var old = await db.TaskHeaderOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct);
            if (b.CanonicalizationVersion != 1 || o.CanonicalizationVersion != 1) {
                if (batch is not null) throw Error("BatchIdentityConflict");
                if (old is not null) throw Error("OperationIdentityConflict");
                throw Error("UnsupportedOperationVersion", 400);
            }
            var payload = BasicInformationCanonical.Operation(taskId, b); var hash = DraftCanonical.Hash(payload);
            var fingerprint = BasicInformationCanonical.Batch(taskId, b, hash);
            if (batch is not null) {
                if (batch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict");
                if (old is null) throw Error("BasicInformationReceiptIntegrityUnavailable", 503);
                var original = Decode<DraftBatchReceipt>(batch.ReceiptJson); var receipt = ScopedHeaderReceipt(old, task, authority.EnvironmentId);
                if (old.ContentHash != hash || original.ReceiptVersion != 6 || original.HeaderOperations is not { Count: 1 } headers || headers[0] != receipt)
                    throw Error("BasicInformationReceiptIntegrityUnavailable", 503);
                return new BasicInformationBatchResult(original, receipt, true);
            }
            if (old is not null) {
                if (old.ContentHash != hash) throw Error("OperationIdentityConflict");
                return new BasicInformationBatchResult(null, ScopedHeaderReceipt(old, task, authority.EnvironmentId), true);
            }
            if (await db.AcceptedOperationIdentities.AnyAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct)) throw Error("OperationIdentityConflict");
            if (o.Kind is not ("CaptureBasicInformationField" or "CorrectBasicInformationField") ||
                (o.Kind == "CaptureBasicInformationField") != (o.Predecessor is null)) throw Error("InvalidBasicInformationOperation", 400);
            if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) throw Error("DraftAcceptanceUnavailable");
            if (o.BaseDraftVersion != b.ExpectedDraftVersion || (task.SharedDraft?.DraftVersion ?? 0) != b.ExpectedDraftVersion) throw Error("DraftVersionConflict");
            var field = o.Field.ToString();
            var projection = await db.TaskHeaderProjections.SingleOrDefaultAsync(x => x.TaskId == taskId && x.Field == field, ct);
            if (o.Kind == "CaptureBasicInformationField") {
                if (projection is not null) throw Error("BasicInformationFieldAlreadyCaptured");
            } else {
                if (projection is null || projection.LastOperationId != o.Predecessor!.OperationId) throw Error("BasicInformationPredecessorConflict");
                var prior = await db.TaskHeaderOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == projection.LastOperationId, ct);
                _ = ScopedHeaderReceipt(prior, task, authority.EnvironmentId);
                if (prior.ContentHash != o.Predecessor.ContentHash.ToUpperInvariant() || prior.Field != field) throw Error("BasicInformationPredecessorConflict");
            }
            var contexts = await Sources(task, b.SourceProof is null ? [] : [b.SourceProof],
                [new(o.OperationId, o.SourceWorkContextId, o.ContributorUserIdAssertion, [])], authority, ct);
            var source = contexts[o.SourceWorkContextId];
            if (b.ExpectedDraftVersion == long.MaxValue) throw Error("DraftVersionExhausted");
            var next = b.ExpectedDraftVersion + 1; var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            var receiptNew = new BasicInformationReceipt(taskId, o.OperationId, b.BatchId, 1, hash, o.Kind, o.Field, o.Value,
                source.Id, source.UserId, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, task.LineId!.Value, 2,
                o.BaseDraftVersion, o.OriginLocalEditVersion, now, next,
                o.Predecessor is null ? null : new(o.Predecessor.OperationId, o.Predecessor.ContentHash.ToUpperInvariant()));
            var root = await db.SharedDrafts.SingleOrDefaultAsync(x => x.TaskId == taskId, ct);
            if (root is null) { root = new() { TaskId = taskId }; db.SharedDrafts.Add(root); await db.SaveChangesAsync(ct); }
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == b.ExpectedDraftVersion && x.DraftVersion < long.MaxValue)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            var outcome = new DraftBatchReceipt(taskId, b.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId, now,
                b.ExpectedDraftVersion, next, [], ReceiptVersion: 6, HeaderOperations: [receiptNew]);
            db.AcceptedBatchReceipts.Add(new() { TaskId = taskId, BatchId = b.BatchId, RequestFingerprint = fingerprint, DraftVersion = next, ReceiptJson = Encode(outcome) });
            await db.SaveChangesAsync(ct);
            db.TaskHeaderOperations.Add(new() { TaskId = taskId, OperationId = o.OperationId, BatchId = b.BatchId, Field = field,
                Kind = o.Kind, Value = o.Value, SourceWorkContextId = source.Id, ContributorUserId = source.UserId,
                ActorUserId = authority.UserId, ActorPersonalSessionId = authority.PersonalSessionId, EnvironmentId = authority.EnvironmentId,
                LineId = task.LineId.Value, BaseDraftVersion = o.BaseDraftVersion, OriginLocalEditVersion = o.OriginLocalEditVersion,
                PredecessorOperationId = o.Predecessor?.OperationId, PredecessorHash = o.Predecessor?.ContentHash.ToUpperInvariant(),
                CanonicalPayload = payload, ContentHash = hash, ReceiptJson = Encode(receiptNew), AcceptedAtUtc = now, DraftVersion = next });
            await db.SaveChangesAsync(ct);
            if (projection is null) db.TaskHeaderProjections.Add(new() { TaskId = taskId, Field = field, LastOperationId = o.OperationId });
            else projection.LastOperationId = o.OperationId;
            await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = taskId, BatchId = b.BatchId, ActorUserId = authority.UserId,
                PersonalSessionId = authority.PersonalSessionId, OccurredAtUtc = now, BeforeVersion = b.ExpectedDraftVersion, AfterVersion = next, NewOperationCount = 1 });
            return new BasicInformationBatchResult(outcome, receiptNew, false);
        }, ct));
    }
    public Task<BasicInformationReceipt> BasicInformationReceiptAsync(Guid taskId, Guid operationId, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async authority => {
            var task = await identity.Target(taskId, null, ct);
            var row = await db.TaskHeaderOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == operationId, ct) ?? throw Error("NotFound", 404);
            return ScopedHeaderReceipt(row, task, authority.EnvironmentId);
        }, ct);
    public Task<BasicInformationOutput> ReadBasicInformationAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct,
        int limit = 20, long? beforeDraftVersion = null, long? expectedDraftVersion = null, Guid? expectedTaskLineId = null) =>
        store.RunAsync(actor, false, taskId, [], async authority => {
            if (limit is < 1 or > 100 || beforeDraftVersion <= 0 || expectedDraftVersion < 0 || expectedTaskLineId == Guid.Empty)
                throw Error("InvalidBasicInformationQuery", 400);
            var task = await identity.Target(taskId, expectedTaskLineId, ct); var version = task.SharedDraft?.DraftVersion ?? 0;
            if (expectedDraftVersion is { } expected && expected != version) throw Error("BasicInformationVersionConflict");
            var current = await (from p in db.TaskHeaderProjections.AsNoTracking() join o in db.TaskHeaderOperations.AsNoTracking()
                on new { p.TaskId, OperationId = p.LastOperationId } equals new { o.TaskId, o.OperationId } where p.TaskId == taskId select o).ToListAsync(ct);
            var rows = await db.TaskHeaderOperations.AsNoTracking().Where(x => x.TaskId == taskId && (!beforeDraftVersion.HasValue || x.DraftVersion < beforeDraftVersion))
                .OrderByDescending(x => x.DraftVersion).Take(limit + 1).ToListAsync(ct);
            if (current.Concat(rows).Any(r => r.DraftVersion > version)) throw Error("BasicInformationReceiptIntegrityUnavailable", 503);
            var currentReceipts = current.Select(r => ScopedHeaderReceipt(r, task, authority.EnvironmentId)).ToDictionary(r => r.Field);
            var fields = Enum.GetValues<BasicInformationField>().Select(field => {
                currentReceipts.TryGetValue(field, out var receipt); var value = receipt?.Value;
                var state = receipt is null ? "Unrecorded" : value is null ? "ExplicitNull" : value.Length == 0 ? "Empty" : string.IsNullOrWhiteSpace(value) ? "Whitespace" : "Provided";
                return new BasicInformationFieldOutput(field, value, state, receipt is not null, receipt);
            }).ToList();
            var history = rows.Take(limit).Select(r => ScopedHeaderReceipt(r, task, authority.EnvironmentId)).ToList();
            return new BasicInformationOutput(taskId, task.LineId!.Value, 2, version, LineSharedTaskEligibility.Availability(task),
                new(fields[0].Value, fields[1].Value, fields[2].Value), fields, fields.Count(x => x.HasAcceptedFact),
                fields.Count(x => x.ValueState == "Provided"), history, rows.Count > limit ? history[^1].DraftVersion : null);
        }, ct);
}
