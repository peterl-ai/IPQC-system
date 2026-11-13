using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    public Task<ItemRemovalBatchResult> RemoveItemAsync(Guid taskId, ItemRemovalBatchInput b, ClaimsPrincipal actor, CancellationToken ct)
    {
        // Records contain no mutable collections. Discovery and validation share this exact input.
        var sources = b.Operation is { } op ? new[] { op.SourceWorkContextId } : [];
        return IdentityBoundary(() => store.RunAsync(actor, false, taskId, sources, async authority => {
            if (b.BatchId == Guid.Empty || b.ExpectedTaskLineId == Guid.Empty || b.ExpectedDraftVersion < 0 ||
                b.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } || b.Operation is not { } o ||
                o.OperationId == Guid.Empty || o.TaskItemId == Guid.Empty || o.InspectionAttemptId == Guid.Empty ||
                o.SourceWorkContextId == Guid.Empty || o.ContributorUserIdAssertion == Guid.Empty || o.BaseDraftVersion < 0 ||
                o.OriginLocalEditVersion < 0 || b.SourceProof is { WorkContextId: var proofId } && proofId != o.SourceWorkContextId)
                throw Error("InvalidItemRemovalBatch", 400);
            var task = await identity.Target(taskId, b.ExpectedTaskLineId, ct);
            if (b.ExpectedWorkflowVersion != (int)task.WorkflowVersion || b.ExpectedEnvironmentId != authority.EnvironmentId)
                throw Error("DraftScopeMismatch");
            var batch = await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == b.BatchId, ct);
            var old = await db.ItemRemovalOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct);
            if (b.CanonicalizationVersion != 1 || o.CanonicalizationVersion != 1) {
                if (batch is not null) throw Error("BatchIdentityConflict");
                if (old is not null) throw Error("OperationIdentityConflict");
                throw Error("UnsupportedOperationVersion", 400);
            }
            var payload = DraftCanonical.ItemRemoval(taskId, b);
            var hash = DraftCanonical.Hash(payload);
            var fingerprint = DraftCanonical.ItemRemovalBatch(taskId, b, hash);
            if (batch is not null) {
                if (batch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict");
                var original = Decode<DraftBatchReceipt>(batch.ReceiptJson);
                return new ItemRemovalBatchResult(original, original.ItemOperations!.Single(), true);
            }
            if (old is not null) {
                if (old.ContentHash != hash) throw Error("OperationIdentityConflict");
                return new ItemRemovalBatchResult(null, Decode<ItemRemovalReceipt>(old.ReceiptJson), true);
            }
            if (await db.SharedOperationIdentities.AnyAsync(x => x.TaskId == taskId && x.OperationId == o.OperationId, ct))
                throw Error("OperationIdentityConflict");
            if (o.Kind != "RemoveItem" || !o.ConfirmedAbsentOrNotApplicable ||
                o.ConfirmationSource is not ("Production" or "Equipment") || o.Reason is { Length: > 500 } || o.Reason?.Any(char.IsControl) == true)
                throw Error("InvalidItemRemoval", 400);
            if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation"))
                throw Error("DraftAcceptanceUnavailable");
            if (o.BaseDraftVersion != b.ExpectedDraftVersion || !Current(task, o.TaskItemId, o.InspectionAttemptId))
                throw Error("DraftAttemptConflict");
            var removed = await db.ItemRemovalOperations.AsNoTracking().Where(x => x.TaskId == taskId).Select(x => x.InspectionAttemptId).ToListAsync(ct);
            if (removed.Contains(o.InspectionAttemptId)) throw Error("DraftItemAlreadyRemoved");
            var contexts = await Sources(task, b.SourceProof is null ? [] : [b.SourceProof],
                [new(o.OperationId, o.SourceWorkContextId, o.ContributorUserIdAssertion, [])], authority, ct);
            var source = contexts[o.SourceWorkContextId];
            if (b.ExpectedDraftVersion == long.MaxValue) throw Error("DraftVersionExhausted");
            var next = b.ExpectedDraftVersion + 1;
            var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            var attempt = task.Items.Single(i => i.Id == o.TaskItemId).InspectionAttempts.Single(a => a.Id == o.InspectionAttemptId);
            var remaining = task.Items.Count(i => i.CurrentInspectionAttempt is { } a && a.InspectionAttemptId != o.InspectionAttemptId && !removed.Contains(a.InspectionAttemptId));
            var receipt = new ItemRemovalReceipt(taskId, o.OperationId, b.BatchId, o.Kind, 1, hash, o.TaskItemId, o.InspectionAttemptId,
                source.Id, source.UserId, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, task.LineId!.Value, 2,
                now, next, "Removed", true, o.ConfirmationSource, o.Reason, attempt.MinimumSampleCount, remaining, remaining == 0);
            var root = await db.SharedDrafts.SingleOrDefaultAsync(x => x.TaskId == taskId, ct);
            if (root is null) { root = new SharedDraft { TaskId = taskId }; db.SharedDrafts.Add(root); await db.SaveChangesAsync(ct); }
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == b.ExpectedDraftVersion && x.DraftVersion < long.MaxValue)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            var outcome = new DraftBatchReceipt(taskId, b.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId, now,
                b.ExpectedDraftVersion, next, [], [receipt], 2);
            db.AcceptedBatchReceipts.Add(new() { TaskId = taskId, BatchId = b.BatchId, RequestFingerprint = fingerprint,
                DraftVersion = next, ReceiptJson = Encode(outcome) });
            await db.SaveChangesAsync(ct);
            db.ItemRemovalOperations.Add(new() { TaskId = taskId, OperationId = o.OperationId, BatchId = b.BatchId,
                TaskItemId = o.TaskItemId, InspectionAttemptId = o.InspectionAttemptId, SourceWorkContextId = source.Id,
                ContributorUserId = source.UserId, UploadedByUserId = authority.UserId, UploadedPersonalSessionId = authority.PersonalSessionId,
                EnvironmentId = authority.EnvironmentId, LineId = task.LineId.Value, AcceptedAtUtc = now, DraftVersion = next,
                CanonicalPayload = payload, ContentHash = hash, ReceiptJson = Encode(receipt) });
            await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = taskId, BatchId = b.BatchId, ActorUserId = authority.UserId,
                PersonalSessionId = authority.PersonalSessionId, OccurredAtUtc = now, BeforeVersion = b.ExpectedDraftVersion,
                AfterVersion = next, NewOperationCount = 1 });
            return new ItemRemovalBatchResult(outcome, receipt, false);
        }, ct));
    }
}
