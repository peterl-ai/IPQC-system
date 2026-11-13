using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    public Task<UnableToMeasureBatchResult> AcceptUnableAsync(Guid taskId, UnableToMeasureBatchInput b, ClaimsPrincipal actor, CancellationToken ct)
    {
        // All nested records are immutable: source discovery and acceptance share one manifest.
        var sources = b.Operation is { } operation ? new[] { operation.SourceWorkContextId } : [];
        return IdentityBoundary(() => store.RunAsync(actor, false, taskId, sources, async authority => {
            UnableShape(b); var o = b.Operation;
            var task = await identity.Target(taskId, b.ExpectedTaskLineId, ct);
            if (b.ExpectedWorkflowVersion != 2 || b.ExpectedEnvironmentId != authority.EnvironmentId) throw Error("DraftScopeMismatch");
            var batch = await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == b.BatchId, ct);
            var old = await db.UnableToMeasureOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct);
            var registered = await db.AcceptedOperationIdentities.AnyAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct);
            if (b.CanonicalizationVersion != 1 || o.CanonicalizationVersion != 1) {
                if (batch is not null) throw Error("BatchIdentityConflict");
                if (old is not null || registered) throw Error("OperationIdentityConflict");
                throw Error("UnsupportedOperationVersion", 400);
            }
            var payload = UnableToMeasureCanonical.Operation(taskId, b); var hash = DraftCanonical.Hash(payload);
            var fingerprint = UnableToMeasureCanonical.Batch(taskId, b, hash);
            if (batch is not null) {
                if (batch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict");
                var original = Decode<DraftBatchReceipt>(batch.ReceiptJson);
                return new UnableToMeasureBatchResult(original, original.UnableOperations!.Single(), true);
            }
            if (old is not null) {
                if (old.ContentHash != hash) throw Error("OperationIdentityConflict");
                return new UnableToMeasureBatchResult(null, Decode<UnableToMeasureReceipt>(old.ReceiptJson), true);
            }
            if (registered) throw Error("OperationIdentityConflict");
            if (o.Kind is not ("SetUnableToMeasureNG" or "CorrectUnableToMeasureNG" or "ClearUnableToMeasureNG")) throw Error("UnsupportedItemDisposition", 400);
            var clear = o.Kind == "ClearUnableToMeasureNG";
            if (clear ? o.NgReason is not null || o.Evidence is not null :
                string.IsNullOrWhiteSpace(o.NgReason) || o.NgReason.Length > 2000 || o.NgReason.Any(char.IsControl) || o.Evidence is null) throw Error("InvalidUnableToMeasure", 400);
            if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) throw Error("DraftAcceptanceUnavailable");
            if (o.BaseDraftVersion != b.ExpectedDraftVersion || !Current(task, o.TaskItemId, o.InspectionAttemptId)) throw Error("DraftAttemptConflict");
            if (await db.ItemRemovalOperations.AnyAsync(x => x.TaskId == taskId && x.InspectionAttemptId == o.InspectionAttemptId, ct)) throw Error("DraftItemRemoved");
            var projection = await db.UnableToMeasureProjections.SingleOrDefaultAsync(x => x.TaskId == taskId && x.InspectionAttemptId == o.InspectionAttemptId, ct);
            if (projection is null) {
                if (o.Kind != "SetUnableToMeasureNG" || o.Predecessor is not null) throw Error("DispositionPredecessorConflict");
            } else {
                if (projection.TaskItemId != o.TaskItemId || o.Predecessor is null || projection.LastOperationId != o.Predecessor.OperationId ||
                    (o.Kind == "SetUnableToMeasureNG") == projection.IsActive) throw Error("DispositionPredecessorConflict");
                var predecessor = await db.UnableToMeasureOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == projection.LastOperationId, ct);
                if (predecessor.ContentHash != o.Predecessor.ContentHash.ToUpperInvariant()) throw Error("DispositionPredecessorConflict");
            }
            var contexts = await Sources(task, b.SourceProof is null ? [] : [b.SourceProof],
                [new(o.OperationId, o.SourceWorkContextId, o.ContributorUserIdAssertion, [])], authority, ct);
            var source = contexts[o.SourceWorkContextId];
            DraftAttachmentReceipt? evidence = null;
            if (!clear) {
                var reference = await db.DraftAttachmentOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == o.Evidence!.OperationId, ct);
                if (reference is null || reference.Kind != "AssociateAttachment" || reference.ContentHash != o.Evidence!.ContentHash.ToUpperInvariant() ||
                    reference.TaskItemId != o.TaskItemId || reference.InspectionAttemptId != o.InspectionAttemptId || reference.LineId != task.LineId ||
                    reference.WorkflowVersion != 2 || reference.EnvironmentId != authority.EnvironmentId) throw Error("UnableEvidenceReferenceConflict");
                if (db.Database.IsNpgsql()) _ = await db.AttachmentAvailabilities.FromSqlInterpolated($"SELECT * FROM \"AttachmentAvailabilities\" WHERE \"AttachmentId\" = {reference.AttachmentId} FOR UPDATE").AsNoTracking().ToListAsync(ct);
                evidence = Decode<DraftAttachmentReceipt>(reference.ReceiptJson);
                var observed = await UnableEvidence(task, o.TaskItemId, o.InspectionAttemptId, evidence, ct);
                if (!observed.IsValid) throw Error("UnableEvidence" + observed.State);
            }
            if (b.ExpectedDraftVersion == long.MaxValue) throw Error("DraftVersionExhausted");
            var next = b.ExpectedDraftVersion + 1; var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            // Genuine accepted reference implies an already existing shared draft root.
            var root = await db.SharedDrafts.SingleAsync(x => x.TaskId == taskId, ct);
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == b.ExpectedDraftVersion && x.DraftVersion < long.MaxValue)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            var minimum = task.Items.Single(x => x.Id == o.TaskItemId).InspectionAttempts.Single(x => x.Id == o.InspectionAttemptId).MinimumSampleCount;
            var prior = o.Predecessor is null ? null : new DraftOperationReference(o.Predecessor.OperationId, o.Predecessor.ContentHash.ToUpperInvariant());
            var receipt = new UnableToMeasureReceipt(taskId, o.OperationId, b.BatchId, 1, hash, o.Kind, o.TaskItemId, o.InspectionAttemptId,
                source.Id, source.UserId, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, task.LineId!.Value, 2, now, next,
                clear ? "Applicable" : "UnableToMeasureNG", clear ? null : "NG", o.NgReason, minimum, evidence, prior);
            var outcome = new DraftBatchReceipt(taskId, b.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId, now, b.ExpectedDraftVersion, next, [], ReceiptVersion: 4, UnableOperations: [receipt]);
            db.AcceptedBatchReceipts.Add(new() { TaskId = taskId, BatchId = b.BatchId, DraftVersion = next, RequestFingerprint = fingerprint, ReceiptJson = Encode(outcome) }); await db.SaveChangesAsync(ct);
            db.UnableToMeasureOperations.Add(new() { TaskId = taskId, OperationId = o.OperationId, BatchId = b.BatchId, TaskItemId = o.TaskItemId, InspectionAttemptId = o.InspectionAttemptId,
                SourceWorkContextId = source.Id, ContributorUserId = source.UserId, ActorUserId = authority.UserId, ActorPersonalSessionId = authority.PersonalSessionId,
                EnvironmentId = authority.EnvironmentId, LineId = task.LineId.Value, Kind = o.Kind, NgReason = o.NgReason,
                EvidenceOperationId = evidence?.OperationId, EvidenceHash = evidence?.ContentHash, AttachmentId = evidence?.AttachmentId,
                PredecessorOperationId = prior?.OperationId, PredecessorHash = prior?.ContentHash, ContentHash = hash, CanonicalPayload = payload,
                ReceiptJson = Encode(receipt), AcceptedAtUtc = now, DraftVersion = next }); await db.SaveChangesAsync(ct);
            if (projection is null) { projection = new() { TaskId = taskId, TaskItemId = o.TaskItemId, InspectionAttemptId = o.InspectionAttemptId }; db.UnableToMeasureProjections.Add(projection); }
            projection.LastOperationId = o.OperationId; projection.IsActive = !clear; await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = taskId, BatchId = b.BatchId, ActorUserId = authority.UserId, PersonalSessionId = authority.PersonalSessionId,
                OccurredAtUtc = now, BeforeVersion = b.ExpectedDraftVersion, AfterVersion = next, NewOperationCount = 1 });
            return new UnableToMeasureBatchResult(outcome, receipt, false);
        }, ct));
    }
    private static void UnableShape(UnableToMeasureBatchInput b)
    {
        bool RefValid(DraftOperationReference? r) => r is null || r.OperationId != Guid.Empty && DraftCanonical.IsHash(r.ContentHash);
        if (b.BatchId == Guid.Empty || b.ExpectedTaskLineId == Guid.Empty || b.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } || b.ExpectedDraftVersion < 0 ||
            b.Operation is not { } o || o.OperationId == Guid.Empty || o.TaskItemId == Guid.Empty || o.InspectionAttemptId == Guid.Empty || o.SourceWorkContextId == Guid.Empty ||
            o.ContributorUserIdAssertion == Guid.Empty || o.BaseDraftVersion < 0 || o.OriginLocalEditVersion < 0 || !RefValid(o.Evidence) || !RefValid(o.Predecessor) ||
            b.SourceProof is { } proof && proof.WorkContextId != o.SourceWorkContextId) throw Error("InvalidUnableBatch", 400);
    }
    private async Task<UnableToMeasureEvidence> UnableEvidence(PatrolTask task, Guid item, Guid attempt, DraftAttachmentReceipt evidence, CancellationToken ct)
    {
        var p = await db.DraftAttachmentProjections.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == task.Id && x.AttachmentId == evidence.AttachmentId, ct);
        var a = await db.InspectionAttachments.AsNoTracking().SingleAsync(x => x.Id == evidence.AttachmentId, ct);
        var availability = await ReferenceAvailability(a, ct);
        var state = a.TaskId != task.Id || a.TaskItemId != item || a.InspectionAttemptId != attempt || a.LineId != task.LineId ||
            a.EnvironmentId != evidence.EnvironmentId || a.WorkflowVersion != 2 ? "ScopeMismatch" : !Current(task, item, attempt) ? "AttemptNotCurrent" : p is not { IsAttached: true } ? "Detached" :
            p.LastOperationId != evidence.OperationId ? "ReferenceSuperseded" : availability != "Ready" ? availability : "Valid";
        return new(evidence.OperationId, a.Id, p?.LastOperationId, p?.IsAttached ?? false, availability, state, state == "Valid");
    }
    public Task<DraftDispositionOutput> ReadDispositionsAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct, long? beforeDraftVersion = null) => store.RunAsync(actor, false, taskId, [], async _ => {
        if (beforeDraftVersion is <= 0) throw Error("InvalidHistoryCursor", 400);
        var task = await identity.Target(taskId, null, ct);
        var projections = await db.UnableToMeasureProjections.AsNoTracking().Where(x => x.TaskId == taskId).ToDictionaryAsync(x => x.InspectionAttemptId, ct);
        var ledger = db.UnableToMeasureOperations.AsNoTracking().Where(x => x.TaskId == taskId);
        var versions = await ledger.Where(x => beforeDraftVersion == null || x.DraftVersion < beforeDraftVersion).Select(x => x.DraftVersion).Distinct().OrderDescending().Take(101).ToListAsync(ct);
        var selected = versions.Take(100).ToArray(); var history = await ledger.Where(x => selected.Contains(x.DraftVersion)).OrderBy(x => x.DraftVersion).ToListAsync(ct);
        var lastIds = projections.Values.Select(x => x.LastOperationId).ToArray(); var last = await ledger.Where(x => lastIds.Contains(x.OperationId)).ToDictionaryAsync(x => x.OperationId, ct);
        var removed = await db.ItemRemovalOperations.AsNoTracking().Where(x => x.TaskId == taskId).Select(x => x.InspectionAttemptId).ToListAsync(ct);
        var items = new List<DraftDispositionItem>();
        foreach (var i in task.Items.OrderBy(x => x.SequenceNo)) {
            var attempt = i.CurrentInspectionAttempt?.InspectionAttemptId; var minimum = i.InspectionAttempts.SingleOrDefault(x => x.Id == attempt)?.MinimumSampleCount;
            UnableToMeasureReceipt? receipt = null; UnableToMeasureEvidence? observed = null;
            var active = attempt is { } id && projections.TryGetValue(id, out var projection) && projection.IsActive;
            if (attempt is { } aid && projections.TryGetValue(aid, out var p)) {
                receipt = Decode<UnableToMeasureReceipt>(last[p.LastOperationId].ReceiptJson);
                if (active && receipt.Evidence is { } e) observed = await UnableEvidence(task, i.Id, aid, e, ct);
            }
            var disposition = attempt is null ? "Unavailable" : attempt is { } scope && removed.Contains(scope) ? "Removed" : active ? "UnableToMeasureNG" : "Applicable";
            items.Add(new(i.Id, attempt, minimum, disposition, active ? receipt?.NgReason : null, receipt, observed,
                attempt is null ? "AttemptUnavailable" : active ? observed?.IsValid == true ? "Satisfied" : "Incomplete" : "NotApplicable"));
        }
        return new DraftDispositionOutput(taskId, task.LineId!.Value, 2, task.SharedDraft?.DraftVersion ?? 0, items,
            history.Select(x => Decode<UnableToMeasureReceipt>(x.ReceiptJson)).ToList(), versions.Count > 100 ? selected.Min() : null);
    }, ct);
}
