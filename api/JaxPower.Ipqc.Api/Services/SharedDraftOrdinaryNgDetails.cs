using System.Security.Claims;
using System.Text;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    private const string OrdinaryNgSet = "SetOrdinaryNgDetails";
    private const string OrdinaryNgCorrect = "CorrectOrdinaryNgDetails";
    private const int OrdinaryNgReceiptVersion = 8;

    private static bool OrdinaryNgText(string? text)
    {
        if (text is null) return true;
        if (text.Length > 2000) return false;
        for (var i = 0; i < text.Length;)
        {
            if (!Rune.TryGetRuneAt(text, i, out var rune) || Rune.IsControl(rune)) return false;
            i += rune.Utf16SequenceLength;
        }
        return true;
    }

    private IQueryable<OrdinaryNgDetailsOperation> OrdinaryNgScope(Guid task, Guid item, Guid attempt) =>
        db.OrdinaryNgDetailsOperations.AsNoTracking()
            .Where(x => x.TaskId == task && x.TaskItemId == item && x.InspectionAttemptId == attempt);

    private Task<OrdinaryNgDetailsOperation?> LatestOrdinaryNgDetails(Guid task, Guid item, Guid attempt, CancellationToken ct) =>
        OrdinaryNgScope(task, item, attempt).OrderByDescending(x => x.DraftVersion).FirstOrDefaultAsync(ct);

    public Task<OrdinaryNgDetailsBatchResult> AcceptOrdinaryNgDetailsAsync(Guid taskId, OrdinaryNgDetailsBatchInput input,
        ClaimsPrincipal actor, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true) throw Error("PersonalSessionRequired", 401);
        var sourceIds = input?.Operation is { } op ? new[] { op.SourceWorkContextId } : [];
        return IdentityBoundary(() => store.RunAsync(actor, false, taskId, sourceIds, async authority =>
        {
            if (input is not { } batch || batch.BatchId == Guid.Empty || batch.ExpectedTaskLineId == Guid.Empty ||
                batch.ExpectedDraftVersion < 0 || batch.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } ||
                batch.Operation is not { } operation || operation.OperationId == Guid.Empty || operation.TaskItemId == Guid.Empty ||
                operation.InspectionAttemptId == Guid.Empty || operation.SourceWorkContextId == Guid.Empty ||
                operation.ContributorUserIdAssertion == Guid.Empty || operation.BaseDraftVersion < 0 || operation.OriginLocalEditVersion < 0 ||
                operation.Predecessor is { } previous && (previous.OperationId == Guid.Empty || !DraftCanonical.IsHash(previous.ContentHash)) ||
                batch.SourceProof is { } proof && proof.WorkContextId != operation.SourceWorkContextId)
                throw Error("InvalidOrdinaryNgDetails", 400);
            if (!OrdinaryNgText(operation.NgType) || !OrdinaryNgText(operation.NgReason) || !OrdinaryNgText(operation.Remarks))
                throw Error("InvalidOrdinaryNgText", 400);

            var task = await identity.Target(taskId, batch.ExpectedTaskLineId, ct);
            if (batch.ExpectedEnvironmentId != authority.EnvironmentId || batch.ExpectedWorkflowVersion != 2)
                throw Error("DraftScopeMismatch");

            var storedBatch = await db.AcceptedBatchReceipts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == batch.BatchId, ct);
            var storedOperation = await db.OrdinaryNgDetailsOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == operation.OperationId, ct);
            var registered = await db.AcceptedOperationIdentities.AnyAsync(x => x.TaskId == taskId && x.OperationId == operation.OperationId, ct);
            if (batch.CanonicalizationVersion != 1 || operation.CanonicalizationVersion != 1)
            {
                if (storedBatch is not null) throw Error("BatchIdentityConflict");
                if (registered || storedOperation is not null) throw Error("OperationIdentityConflict");
                throw Error("UnsupportedOperationVersion", 400);
            }

            var payload = OrdinaryNgDetailsCanonical.Operation(taskId, batch);
            var hash = DraftCanonical.Hash(payload);
            var fingerprint = OrdinaryNgDetailsCanonical.Batch(taskId, batch, hash);
            if (storedBatch is not null)
            {
                if (storedBatch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict");
                if (storedOperation is null || storedOperation.BatchId != batch.BatchId || storedOperation.ContentHash != hash)
                    throw Error("OrdinaryNgDetailsReceiptIntegrityUnavailable", 503);
                var original = Decode<DraftBatchReceipt>(storedBatch.ReceiptJson);
                var receipt = VerifiedOrdinaryNgReceipt(storedOperation, task, authority.EnvironmentId);
                if (original.ReceiptVersion != OrdinaryNgReceiptVersion ||
                    original.OrdinaryNgDetailsOperations is not { Count: 1 } accepted || Encode(accepted[0]) != Encode(receipt))
                    throw Error("OrdinaryNgDetailsReceiptIntegrityUnavailable", 503);
                return new OrdinaryNgDetailsBatchResult(original, receipt, true);
            }
            if (storedOperation is not null)
            {
                if (storedOperation.ContentHash != hash || storedOperation.BaseDraftVersion != batch.ExpectedDraftVersion)
                    throw Error("OperationIdentityConflict");
                return new OrdinaryNgDetailsBatchResult(null, VerifiedOrdinaryNgReceipt(storedOperation, task, authority.EnvironmentId), true);
            }
            if (registered) throw Error("OperationIdentityConflict");

            if (operation.Kind is not (OrdinaryNgSet or OrdinaryNgCorrect) ||
                (operation.Kind == OrdinaryNgSet) != (operation.Predecessor is null))
                throw Error("InvalidOrdinaryNgOperation", 400);
            var expectedVersion = batch.ExpectedDraftVersion;
            if (operation.BaseDraftVersion != expectedVersion || (task.SharedDraft?.DraftVersion ?? 0) != expectedVersion)
                throw Error("DraftVersionConflict");
            if (!Current(task, operation.TaskItemId, operation.InspectionAttemptId)) throw Error("DraftAttemptConflict");
            var basis = await CurrentNg(task, operation.TaskItemId, operation.InspectionAttemptId, ct);
            if (basis.Kind != "OrdinaryNG") throw Error("OrdinaryNgItemNotEligible");

            var latest = await LatestOrdinaryNgDetails(taskId, operation.TaskItemId, operation.InspectionAttemptId, ct);
            var predecessor = operation.Predecessor is { } reference
                ? new DraftOperationReference(reference.OperationId, reference.ContentHash.ToUpperInvariant()) : null;
            if (predecessor is null ? latest is not null :
                latest is null || latest.OperationId != predecessor.OperationId || latest.ContentHash != predecessor.ContentHash)
                throw Error("OrdinaryNgPredecessorConflict");
            if (latest is not null) _ = VerifiedOrdinaryNgReceipt(latest, task, authority.EnvironmentId);

            var contexts = await Sources(task, batch.SourceProof is null ? [] : [batch.SourceProof],
                [new(operation.OperationId, operation.SourceWorkContextId, operation.ContributorUserIdAssertion, [])], authority, ct);
            var source = contexts[operation.SourceWorkContextId];
            if (expectedVersion == long.MaxValue) throw Error("DraftVersionExhausted");

            var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            var next = expectedVersion + 1;
            var acceptedReceipt = new OrdinaryNgDetailsReceipt(taskId, operation.OperationId, batch.BatchId, 1, hash, operation.Kind,
                operation.TaskItemId, operation.InspectionAttemptId, source.Id, source.UserId, authority.UserId, authority.PersonalSessionId,
                authority.EnvironmentId, task.LineId!.Value, 2, operation.BaseDraftVersion, operation.OriginLocalEditVersion,
                now, next, predecessor, operation.NgType, operation.NgReason, operation.Remarks, basis.References);

            // An ordinary NG basis always has an accepted sample and therefore an existing draft root.
            var root = await db.SharedDrafts.SingleAsync(x => x.TaskId == taskId, ct);
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == expectedVersion)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1)
                throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);

            var common = new DraftBatchReceipt(taskId, batch.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId,
                now, expectedVersion, next, [], ReceiptVersion: OrdinaryNgReceiptVersion, OrdinaryNgDetailsOperations: [acceptedReceipt]);
            db.AcceptedBatchReceipts.Add(new() {
                TaskId = taskId, BatchId = batch.BatchId, RequestFingerprint = fingerprint, DraftVersion = next, ReceiptJson = Encode(common)
            });
            await db.SaveChangesAsync(ct);
            db.OrdinaryNgDetailsOperations.Add(new() {
                TaskId = taskId, OperationId = operation.OperationId, BatchId = batch.BatchId, TaskItemId = operation.TaskItemId,
                InspectionAttemptId = operation.InspectionAttemptId, SourceWorkContextId = source.Id, ContributorUserId = source.UserId,
                ActorUserId = authority.UserId, ActorPersonalSessionId = authority.PersonalSessionId, EnvironmentId = authority.EnvironmentId,
                LineId = task.LineId.Value, Kind = operation.Kind, BaseDraftVersion = operation.BaseDraftVersion,
                OriginLocalEditVersion = operation.OriginLocalEditVersion, PredecessorOperationId = predecessor?.OperationId,
                PredecessorHash = predecessor?.ContentHash, NgType = operation.NgType, NgReason = operation.NgReason,
                Remarks = operation.Remarks, NgBasisJson = Encode(basis.References), ContentHash = hash, CanonicalPayload = payload,
                ReceiptJson = Encode(acceptedReceipt), AcceptedAtUtc = now, DraftVersion = next
            });
            await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() {
                Id = Guid.NewGuid(), TaskId = taskId, BatchId = batch.BatchId, ActorUserId = authority.UserId,
                PersonalSessionId = authority.PersonalSessionId, OccurredAtUtc = now, BeforeVersion = expectedVersion,
                AfterVersion = next, NewOperationCount = 1
            });
            return new OrdinaryNgDetailsBatchResult(common, acceptedReceipt, false);
        }, ct));
    }
}
