using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    public Task<SampleEvidenceBatchResult> AcceptSampleEvidenceAsync(Guid taskId, SampleEvidenceBatchInput b, ClaimsPrincipal actor, CancellationToken ct)
    {
        var sources = b.Operation is { } o ? new[] { o.SourceWorkContextId } : [];
        return IdentityBoundary(() => store.RunAsync(actor, false, taskId, sources, async authority => {
            BindingShape(b); var op = b.Operation; var task = await identity.Target(taskId, b.ExpectedTaskLineId, ct);
            if (b.ExpectedEnvironmentId != authority.EnvironmentId || b.ExpectedWorkflowVersion != 2) throw Error("DraftScopeMismatch");
            var batch = await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == b.BatchId, ct);
            var old = await db.SampleEvidenceOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == op.OperationId, ct);
            var registered = await db.AcceptedOperationIdentities.AnyAsync(x => x.TaskId == taskId && x.OperationId == op.OperationId, ct);
            if (b.CanonicalizationVersion != 1 || op.CanonicalizationVersion != 1) {
                if (batch is not null) throw Error("BatchIdentityConflict"); if (registered) throw Error("OperationIdentityConflict"); throw Error("UnsupportedOperationVersion", 400);
            }
            var payload = SampleEvidenceCanonical.Operation(taskId, b); var hash = DraftCanonical.Hash(payload); var fingerprint = SampleEvidenceCanonical.Batch(taskId, b, hash);
            if (batch is not null) { if (batch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict"); var original = Decode<DraftBatchReceipt>(batch.ReceiptJson); return new SampleEvidenceBatchResult(original, original.SampleEvidenceOperations!.Single(), true); }
            if (old is not null) { if (old.ContentHash != hash) throw Error("OperationIdentityConflict"); return new SampleEvidenceBatchResult(null, Decode<SampleEvidenceReceipt>(old.ReceiptJson), true); }
            if (registered) throw Error("OperationIdentityConflict");
            if (op.Kind is not ("BindSampleEvidence" or "CorrectSampleEvidence" or "WithdrawSampleEvidence")) throw Error("UnsupportedSampleEvidenceOperation", 400);
            var withdraw = op.Kind == "WithdrawSampleEvidence";
            if (withdraw ? op.PhotoReference is not null : op.PhotoReference is null) throw Error("InvalidSampleEvidencePhoto", 400);
            if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) throw Error("DraftAcceptanceUnavailable");
            if (op.BaseDraftVersion != b.ExpectedDraftVersion || !Current(task, op.TaskItemId, op.InspectionAttemptId)) throw Error("DraftAttemptConflict");
            var projection = await db.SampleEvidenceProjections.SingleOrDefaultAsync(x => x.TaskId == taskId && x.BindingId == op.BindingId, ct);
            if (projection is null) {
                if (op.Kind != "BindSampleEvidence" || op.BindingId != op.OperationId || op.Predecessor is not null) throw Error("BindingPredecessorConflict");
            } else {
                if (op.Kind == "BindSampleEvidence" || !projection.IsActive || op.Predecessor is null || projection.LastOperationId != op.Predecessor.OperationId) throw Error("BindingPredecessorConflict");
                var prior = await db.SampleEvidenceOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == projection.LastOperationId, ct);
                if (prior.ContentHash != op.Predecessor.ContentHash.ToUpperInvariant()) throw Error("BindingPredecessorConflict");
                if (prior.TaskItemId != op.TaskItemId || prior.InspectionAttemptId != op.InspectionAttemptId || prior.SampleOperationId != op.SampleOperation.OperationId || prior.SampleHash != op.SampleOperation.ContentHash.ToUpperInvariant()) throw Error("BindingTargetConflict");
            }
            // Current-attempt accepted historical targets are valid facts; no invented latest-value or physical-time claim.
            var transition = await BindingTransition(taskId, op.TaskItemId, op.InspectionAttemptId, op.SampleOperation, ct);
            if (!withdraw && (await db.ItemRemovalOperations.AnyAsync(x => x.TaskId == taskId && x.InspectionAttemptId == op.InspectionAttemptId, ct) ||
                await db.UnableToMeasureProjections.AnyAsync(x => x.TaskId == taskId && x.InspectionAttemptId == op.InspectionAttemptId && x.IsActive, ct))) throw Error("BindingItemNotApplicable");
            var contexts = await Sources(task, b.SourceProof is null ? [] : [b.SourceProof], [new(op.OperationId, op.SourceWorkContextId, op.ContributorUserIdAssertion, [])], authority, ct);
            var source = contexts[op.SourceWorkContextId]; DraftAttachmentReceipt? photo = null;
            if (!withdraw) {
                var row = await db.DraftAttachmentOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == op.PhotoReference!.OperationId, ct);
                if (row is null || row.Kind != "AssociateAttachment" || row.ContentHash != op.PhotoReference!.ContentHash.ToUpperInvariant() ||
                    row.TaskItemId != op.TaskItemId || row.InspectionAttemptId != op.InspectionAttemptId || row.EnvironmentId != authority.EnvironmentId || row.LineId != task.LineId || row.WorkflowVersion != 2) throw Error("BindingPhotoReferenceConflict");
                if (db.Database.IsNpgsql()) _ = await db.AttachmentAvailabilities.FromSqlInterpolated($"SELECT * FROM \"AttachmentAvailabilities\" WHERE \"AttachmentId\"={row.AttachmentId} FOR UPDATE").AsNoTracking().ToListAsync(ct);
                photo = Decode<DraftAttachmentReceipt>(row.ReceiptJson);
                var observed = await UnableEvidence(task, op.TaskItemId, op.InspectionAttemptId, photo, ct);
                if (!observed.IsValid) throw Error("BindingPhoto" + observed.State);
            }
            if (b.ExpectedDraftVersion == long.MaxValue) throw Error("DraftVersionExhausted");
            var next = b.ExpectedDraftVersion + 1; var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            var root = await db.SharedDrafts.SingleAsync(x => x.TaskId == taskId, ct);
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == b.ExpectedDraftVersion && x.DraftVersion < long.MaxValue).ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            var predecessor = op.Predecessor is null ? null : new DraftOperationReference(op.Predecessor.OperationId, op.Predecessor.ContentHash.ToUpperInvariant());
            var receipt = new SampleEvidenceReceipt(taskId, op.OperationId, b.BatchId, op.BindingId, 1, hash, op.Kind, op.TaskItemId, op.InspectionAttemptId,
                transition.Operation.SampleId, transition, photo, source.Id, source.UserId, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, task.LineId!.Value, 2, now, next, predecessor, !withdraw);
            var outcome = new DraftBatchReceipt(taskId, b.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId, now, b.ExpectedDraftVersion, next, [], ReceiptVersion: 5, SampleEvidenceOperations: [receipt]);
            db.AcceptedBatchReceipts.Add(new() { TaskId = taskId, BatchId = b.BatchId, RequestFingerprint = fingerprint, DraftVersion = next, ReceiptJson = Encode(outcome) }); await db.SaveChangesAsync(ct);
            db.SampleEvidenceOperations.Add(new() { TaskId = taskId, OperationId = op.OperationId, BatchId = b.BatchId, BindingId = op.BindingId,
                TaskItemId = op.TaskItemId, InspectionAttemptId = op.InspectionAttemptId, SampleId = transition.Operation.SampleId, SampleOperationId = transition.Operation.OperationId,
                SampleHash = transition.Operation.ContentHash, TransitionKind = transition.Classification, PhotoReferenceOperationId = photo?.OperationId, AttachmentId = photo?.AttachmentId,
                PhotoReferenceHash = photo?.ContentHash, SourceWorkContextId = source.Id, ContributorUserId = source.UserId, ActorUserId = authority.UserId,
                ActorPersonalSessionId = authority.PersonalSessionId, EnvironmentId = authority.EnvironmentId, LineId = task.LineId.Value, Kind = op.Kind,
                PredecessorOperationId = predecessor?.OperationId, PredecessorHash = predecessor?.ContentHash, ContentHash = hash, CanonicalPayload = payload, ReceiptJson = Encode(receipt), AcceptedAtUtc = now, DraftVersion = next }); await db.SaveChangesAsync(ct);
            if (projection is null) { projection = new() { TaskId = taskId, BindingId = op.BindingId }; db.SampleEvidenceProjections.Add(projection); }
            projection.LastOperationId = op.OperationId; projection.IsActive = !withdraw; await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = taskId, BatchId = b.BatchId, ActorUserId = authority.UserId, PersonalSessionId = authority.PersonalSessionId, OccurredAtUtc = now, BeforeVersion = b.ExpectedDraftVersion, AfterVersion = next, NewOperationCount = 1 });
            return new SampleEvidenceBatchResult(outcome, receipt, false);
        }, ct));
    }
    private static void BindingShape(SampleEvidenceBatchInput b) {
        bool Ref(DraftOperationReference? r) => r is null || r.OperationId != Guid.Empty && DraftCanonical.IsHash(r.ContentHash);
        if (b.BatchId == Guid.Empty || b.ExpectedTaskLineId == Guid.Empty || b.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } || b.ExpectedDraftVersion < 0 ||
            b.Operation is not { } o || o.OperationId == Guid.Empty || o.BindingId == Guid.Empty || o.TaskItemId == Guid.Empty || o.InspectionAttemptId == Guid.Empty ||
            o.SourceWorkContextId == Guid.Empty || o.ContributorUserIdAssertion == Guid.Empty || o.BaseDraftVersion < 0 || o.OriginLocalEditVersion < 0 || o.SampleOperation is null ||
            !Ref(o.SampleOperation) || !Ref(o.PhotoReference) || !Ref(o.Predecessor) || b.SourceProof is { } p && p.WorkContextId != o.SourceWorkContextId) throw Error("InvalidSampleEvidenceBatch", 400);
    }
    private async Task<AcceptedSampleTransition> BindingTransition(Guid task, Guid item, Guid attempt, DraftOperationReference reference, CancellationToken ct) {
        var row = await db.ContributionOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == task && x.OperationId == reference.OperationId, ct);
        if (row is null || row.ContentHash != reference.ContentHash.ToUpperInvariant() || row.TaskItemId != item || row.InspectionAttemptId != attempt || row.CanonicalizationVersion != 1) throw Error("BindingSampleOperationConflict");
        var receipt = Receipt(row); var deps = await db.ContributionDependencies.AsNoTracking().Where(x => x.TaskId == task && x.OperationId == row.OperationId).Take(2).ToListAsync(ct);
        DraftOperationReceipt? prior = null;
        if (row.Kind == "CorrectSample" && deps.Count == 1) {
            var d = deps[0]; var previous = await db.ContributionOperations.AsNoTracking().SingleAsync(x => x.TaskId == task && x.OperationId == d.DependencyId, ct);
            if (previous.SampleId != row.SampleId || previous.TaskItemId != item || previous.InspectionAttemptId != attempt || previous.ContentHash != d.ContentHash) throw Error("BindingSamplePredecessorConflict");
            prior = Receipt(previous);
        } else if (row.Kind != "CaptureSample" || deps.Count != 0) throw Error("BindingSampleTransitionUnsupported");
        var from = prior?.Value?.Judgment; var to = receipt.Value?.Judgment;
        var kind = row.Kind == "CaptureSample" && to == "NG" ? "InitialNGCapture" : from == "NG" && to == "OK" ? "NGToOKCorrection" :
            from == "NG" && to == "NG" ? "NGToNGCorrection" : from == "OK" && to == "NG" ? "OKToNGCorrection" : null;
        if (kind is null || receipt.IsRemoved || prior?.IsRemoved == true) throw Error("BindingSampleTransitionUnsupported");
        return new(kind, receipt, prior);
    }
    private async Task<string> BindingAvailability(InspectionAttachment a, CancellationToken ct, Dictionary<Guid, string> observations) {
        if (observations.TryGetValue(a.Id, out var known)) return known;
        var current = await ReferenceAvailability(a, ct); observations.Add(a.Id, current); return current;
    }
    private async Task<SampleEvidenceOutput> BindingObservation(PatrolTask task, SampleEvidenceOperation row, bool active, CancellationToken ct, Dictionary<Guid, string>? observations = null) {
        var r = Decode<SampleEvidenceReceipt>(row.ReceiptJson); var current = Current(task, row.TaskItemId, row.InspectionAttemptId);
        var sample = await db.SharedSampleProjections.AsNoTracking().SingleAsync(x => x.TaskId == task.Id && x.SampleId == row.SampleId, ct);
        var applicable = current && !await db.ItemRemovalOperations.AnyAsync(x => x.TaskId == task.Id && x.InspectionAttemptId == row.InspectionAttemptId, ct) &&
            !await db.UnableToMeasureProjections.AnyAsync(x => x.TaskId == task.Id && x.InspectionAttemptId == row.InspectionAttemptId && x.IsActive, ct);
        var photo = active && r.PhotoReference is { } p ? await UnableEvidence(task, row.TaskItemId, row.InspectionAttemptId, p, ct, observations) : null;
        return new(r, current, applicable && !sample.IsRemoved && sample.ValueOperationId == row.SampleOperationId, applicable && photo?.IsValid == true,
            !active ? "Withdrawn" : !current ? "AttemptNotCurrent" : !applicable ? "ItemNotApplicable" : photo?.State ?? "PhotoUnavailable");
    }
    public Task<SampleEvidenceReadOutput> ReadSampleEvidenceAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct, int page = 1, int pageSize = 50, long? beforeDraftVersion = null) => store.RunAsync(actor, false, taskId, [], async _ => {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue || beforeDraftVersion is <= 0) throw Error("InvalidBindingReadQuery", 400);
        var task = await identity.Target(taskId, null, ct); var projections = db.SampleEvidenceProjections.AsNoTracking().Where(x => x.TaskId == taskId);
        var total = await projections.CountAsync(ct); var selected = await projections.OrderBy(x => x.BindingId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var bindings = new List<SampleEvidenceOutput>(); var observations = new Dictionary<Guid, string>();
        foreach (var p in selected) bindings.Add(await BindingObservation(task, await db.SampleEvidenceOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == p.LastOperationId, ct), p.IsActive, ct, observations));
        var ledger = db.SampleEvidenceOperations.AsNoTracking().Where(x => x.TaskId == taskId);
        var history = await ledger.Where(x => beforeDraftVersion == null || x.DraftVersion < beforeDraftVersion).OrderByDescending(x => x.DraftVersion).Take(101).ToListAsync(ct);
        return new SampleEvidenceReadOutput(taskId, task.SharedDraft?.DraftVersion ?? 0, page, pageSize, total, bindings,
            history.Take(100).OrderBy(x => x.DraftVersion).Select(x => Decode<SampleEvidenceReceipt>(x.ReceiptJson)).ToList(), history.Count > 100 ? history[99].DraftVersion : null);
    }, ct);
}
