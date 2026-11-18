using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    public Task<NgPicBatchResult> AcceptNgPicsAsync(Guid taskId, NgPicBatchInput input, ClaimsPrincipal actor, CancellationToken ct) {
        if (actor.Identity?.IsAuthenticated != true) throw Error("PersonalSessionRequired", 401);
        // Capture the request-owned set synchronously, before account discovery or any lock wait.
        var b = input is null ? null : input with { Operation = input.Operation is null ? null! : input.Operation with { PicIds = input.Operation.PicIds?.ToList()! } };
        var sources = b?.Operation is { } op ? new[] { op.SourceWorkContextId } : [];
        return IdentityBoundary(() => store.RunAsync(actor, false, taskId, sources, async authority => {
            if (b is null || b.BatchId == Guid.Empty || b.ExpectedTaskLineId == Guid.Empty || b.ExpectedDraftVersion < 0 || b.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } ||
                b.Operation is not { } o || o.OperationId == Guid.Empty || o.TaskItemId == Guid.Empty || o.InspectionAttemptId == Guid.Empty ||
                o.SourceWorkContextId == Guid.Empty || o.ContributorUserIdAssertion == Guid.Empty || o.BaseDraftVersion < 0 || o.OriginLocalEditVersion < 0 ||
                o.PicIds is null || o.PicIds.Count > 100 || o.PicIds.Any(x => x == Guid.Empty) || o.PicIds.Distinct().Count() != o.PicIds.Count ||
                o.Predecessor is { } p && (p.OperationId == Guid.Empty || !DraftCanonical.IsHash(p.ContentHash)) || b.SourceProof is { } proof && proof.WorkContextId != o.SourceWorkContextId)
                throw Error("InvalidNgPicSelection", 400);
            var task = await identity.Target(taskId, b.ExpectedTaskLineId, ct);
            if (b.ExpectedEnvironmentId != authority.EnvironmentId || b.ExpectedWorkflowVersion != 2) throw Error("DraftScopeMismatch");
            var batch = await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == b.BatchId, ct);
            var old = await db.NgPicSelectionOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct);
            var registered = await db.AcceptedOperationIdentities.AnyAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct);
            if (b.CanonicalizationVersion != 1 || o.CanonicalizationVersion != 1) {
                if (batch is not null) throw Error("BatchIdentityConflict"); if (registered || old is not null) throw Error("OperationIdentityConflict"); throw Error("UnsupportedOperationVersion", 400);
            }
            var payload = NgPicSelectionCanonical.Operation(taskId, b); var hash = DraftCanonical.Hash(payload); var fingerprint = NgPicSelectionCanonical.Batch(taskId, b, hash);
            if (batch is not null) {
                if (batch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict");
                if (old is null || old.ContentHash != hash) throw Error("NgPicReceiptIntegrityUnavailable", 503);
                var original = Decode<DraftBatchReceipt>(batch.ReceiptJson); var receipt = await NgPicReceipt(old, task, authority.EnvironmentId, ct);
                if (original.ReceiptVersion != 7 || original.PicSelectionOperations is not { Count: 1 } pics || Encode(pics[0]) != Encode(receipt)) throw Error("NgPicReceiptIntegrityUnavailable", 503);
                return new NgPicBatchResult(original, receipt, true);
            }
            if (old is not null) {
                if (old.ContentHash != hash) throw Error("OperationIdentityConflict");
                return new NgPicBatchResult(null, await NgPicReceipt(old, task, authority.EnvironmentId, ct), true);
            }
            if (registered) throw Error("OperationIdentityConflict");
            if (o.Kind is not ("SelectNgPics" or "CorrectNgPics") || (o.Kind == "SelectNgPics") != (o.Predecessor is null)) throw Error("InvalidNgPicOperation", 400);
            if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) throw Error("DraftAcceptanceUnavailable");
            if (o.BaseDraftVersion != b.ExpectedDraftVersion || (task.SharedDraft?.DraftVersion ?? 0) != b.ExpectedDraftVersion) throw Error("DraftVersionConflict");
            if (!Current(task, o.TaskItemId, o.InspectionAttemptId)) throw Error("DraftAttemptConflict");
            var projection = await db.NgPicSelectionProjections.SingleOrDefaultAsync(x => x.TaskId == taskId && x.TaskItemId == o.TaskItemId && x.InspectionAttemptId == o.InspectionAttemptId, ct);
            if (o.Kind == "SelectNgPics") { if (projection is not null) throw Error("NgPicPredecessorConflict"); }
            else {
                if (projection is null || projection.LastOperationId != o.Predecessor!.OperationId) throw Error("NgPicPredecessorConflict");
                var prior = await db.NgPicSelectionOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == projection.LastOperationId, ct);
                _ = await NgPicReceipt(prior, task, authority.EnvironmentId, ct);
                if (prior.ContentHash != o.Predecessor.ContentHash.ToUpperInvariant() || prior.TaskItemId != o.TaskItemId || prior.InspectionAttemptId != o.InspectionAttemptId) throw Error("NgPicPredecessorConflict");
            }
            var basis = await CurrentNg(task, o.TaskItemId, o.InspectionAttemptId, ct);
            if (!IsNg(basis)) throw Error("NgPicItemNotEligible");
            var contexts = await Sources(task, b.SourceProof is null ? [] : [b.SourceProof], [new(o.OperationId, o.SourceWorkContextId, o.ContributorUserIdAssertion, [])], authority, ct);
            var source = contexts[o.SourceWorkContextId]; var ids = NgPicSelectionCanonical.Sorted(o.PicIds);
            // Keep existing account -> line -> Task -> context order. PIC locks come last.
            foreach (var id in ids) if (db.Database.IsNpgsql()) _ = await db.PicRosterEntries.FromSqlInterpolated($"SELECT * FROM \"PicRosterEntries\" WHERE \"PicId\"={id} FOR UPDATE").AsNoTracking().ToListAsync(ct);
            var roster = await db.PicRosterRevisions.AsNoTracking().Where(x => ids.Contains(x.PicId) && x.Revision == db.PicRosterRevisions.Where(r => r.PicId == x.PicId).Max(r => r.Revision)).ToListAsync(ct);
            if (roster.Count != ids.Count || roster.Any(x => !x.IsActive)) throw Error("NgPicNotSelectable");
            var selected = roster.OrderBy(x => DraftCanonical.Key(x.PicId), StringComparer.Ordinal).Select(x => new NgPicSnapshot(x.PicId, x.Revision, x.Name, x.Department)).ToList();
            if (b.ExpectedDraftVersion == long.MaxValue) throw Error("DraftVersionExhausted");
            var next = b.ExpectedDraftVersion + 1; var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            var predecessor = o.Predecessor is null ? null : new DraftOperationReference(o.Predecessor.OperationId, o.Predecessor.ContentHash.ToUpperInvariant());
            var accepted = new NgPicSelectionReceipt(taskId, o.OperationId, b.BatchId, 1, hash, o.Kind, o.TaskItemId, o.InspectionAttemptId,
                source.Id, source.UserId, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, task.LineId!.Value, 2,
                o.BaseDraftVersion, o.OriginLocalEditVersion, now, next, predecessor, selected, basis);
            var root = await db.SharedDrafts.SingleAsync(x => x.TaskId == taskId, ct);
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == b.ExpectedDraftVersion && x.DraftVersion < long.MaxValue)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            var outcome = new DraftBatchReceipt(taskId, b.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId, now, b.ExpectedDraftVersion, next, [], ReceiptVersion: 7, PicSelectionOperations: [accepted]);
            db.AcceptedBatchReceipts.Add(new() { TaskId = taskId, BatchId = b.BatchId, RequestFingerprint = fingerprint, DraftVersion = next, ReceiptJson = Encode(outcome) }); await db.SaveChangesAsync(ct);
            db.NgPicSelectionOperations.Add(new() { TaskId = taskId, OperationId = o.OperationId, BatchId = b.BatchId, TaskItemId = o.TaskItemId, InspectionAttemptId = o.InspectionAttemptId,
                SourceWorkContextId = source.Id, ContributorUserId = source.UserId, ActorUserId = authority.UserId, ActorPersonalSessionId = authority.PersonalSessionId,
                EnvironmentId = authority.EnvironmentId, LineId = task.LineId.Value, Kind = o.Kind, BaseDraftVersion = o.BaseDraftVersion, OriginLocalEditVersion = o.OriginLocalEditVersion,
                PredecessorOperationId = predecessor?.OperationId, PredecessorHash = predecessor?.ContentHash, PicCount = selected.Count, NgKind = basis.Kind, NgBasisJson = Encode(basis),
                ContentHash = hash, CanonicalPayload = payload, ReceiptJson = Encode(accepted), AcceptedAtUtc = now, DraftVersion = next }); await db.SaveChangesAsync(ct);
            db.NgPicSelectionMembers.AddRange(selected.Select(p => new NgPicSelectionMember { TaskId = taskId, OperationId = o.OperationId, PicId = p.PicId, RosterRevision = p.RosterRevision, Name = p.Name, Department = p.Department })); await db.SaveChangesAsync(ct);
            if (projection is null) db.NgPicSelectionProjections.Add(new() { TaskId = taskId, TaskItemId = o.TaskItemId, InspectionAttemptId = o.InspectionAttemptId, LastOperationId = o.OperationId });
            else projection.LastOperationId = o.OperationId;
            await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = taskId, BatchId = b.BatchId, ActorUserId = authority.UserId, PersonalSessionId = authority.PersonalSessionId,
                OccurredAtUtc = now, BeforeVersion = b.ExpectedDraftVersion, AfterVersion = next, NewOperationCount = 1 });
            return new NgPicBatchResult(outcome, accepted, false);
        }, ct));
    }
}
