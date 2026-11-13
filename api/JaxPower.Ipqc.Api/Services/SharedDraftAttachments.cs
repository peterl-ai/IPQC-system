using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    // Translate only this additive registry's native identity failure, after full rollback.
    // Old services retain their original SQL/schema dependencies for historical migration fixtures.
    private static async Task<T> IdentityBoundary<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException { SqlState: "23505", ConstraintName: "PK_DraftOperationKeys" or "PK_AcceptedOperationIdentities" } or
                Npgsql.PostgresException { SqlState: "23514", MessageText: "OperationIdentityConflict" } ||
            ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 1811 } sql && sql.Message.Contains("OperationIdentityConflict", StringComparison.Ordinal)) {
            throw Error("OperationIdentityConflict");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23514", MessageText: "DraftItemUnableToMeasureNG" } ||
            ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 1811 } sql && sql.Message.Contains("DraftItemUnableToMeasureNG", StringComparison.Ordinal)) {
            throw Error("DraftItemUnableToMeasureNG");
        }
    }

    public async Task<DraftAttachmentBatchResult> AcceptAttachmentsAsync(Guid taskId, DraftAttachmentBatchInput input, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true) throw Error("PersonalSessionRequired", 401);
        var b = input with { Operations = input.Operations?.ToList()!, SourceProofs = input.SourceProofs?.ToList()! };
        // Immutable discovery only. No disclosed resource data or authority decision before the store.
        var attachmentIds = b.Operations?.Take(11).Where(x => x is not null).Select(x => x.AttachmentId).Distinct().ToArray() ?? [];
        var photoSources = await db.InspectionAttachments.AsNoTracking().Where(x => x.TaskId == taskId && attachmentIds.Contains(x.Id)).Select(x => x.SourceWorkContextId).ToArrayAsync(ct);
        var contextIds = photoSources.Concat(b.Operations?.Take(11).Where(x => x is not null).Select(x => x.SourceWorkContextId) ?? []).Distinct().ToArray();
        return await IdentityBoundary(() => store.RunAsync(actor, false, taskId, contextIds, async authority => {
            AttachmentShape(b);
            var operations = b.Operations!;
            var task = await identity.Target(taskId, b.ExpectedTaskLineId, ct);
            if (b.ExpectedWorkflowVersion != 2 || b.ExpectedEnvironmentId != authority.EnvironmentId) throw Error("DraftScopeMismatch");
            var batch = await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.BatchId == b.BatchId, ct);
            var ids = operations.Select(x => x.OperationId).ToArray();
            var keys = await db.DraftOperationKeys.AsNoTracking().Where(x => x.TaskId == taskId && ids.Contains(x.OperationId)).ToListAsync(ct);
            if (keys.Any(x => x.Domain != "AttachmentReference")) throw Error("OperationIdentityConflict");
            if (b.CanonicalizationVersion != 1 || operations.Any(x => x.CanonicalizationVersion != 1)) {
                if (batch is not null) throw Error("BatchIdentityConflict");
                if (keys.Count > 0) throw Error("OperationIdentityConflict");
                throw Error("UnsupportedOperationVersion", 400);
            }
            var payloads = operations.ToDictionary(x => x.OperationId, x => DraftAttachmentCanonical.Operation(taskId, b, x));
            var hashes = payloads.ToDictionary(x => x.Key, x => DraftCanonical.Hash(x.Value));
            var fingerprint = DraftAttachmentCanonical.Batch(taskId, b, hashes);
            if (batch is not null) {
                if (batch.RequestFingerprint != fingerprint) throw Error("BatchIdentityConflict");
                var original = Decode<DraftBatchReceipt>(batch.ReceiptJson);
                return new DraftAttachmentBatchResult(original, original.AttachmentOperations!, true);
            }
            var known = await db.DraftAttachmentOperations.AsNoTracking().Where(x => x.TaskId == taskId && ids.Contains(x.OperationId)).ToDictionaryAsync(x => x.OperationId, ct);
            foreach (var o in operations) if (known.TryGetValue(o.OperationId, out var old) && old.ContentHash != hashes[o.OperationId]) throw Error("OperationIdentityConflict");
            var fresh = operations.Where(x => !known.ContainsKey(x.OperationId)).ToList();
            var receipts = known.Values.Select(x => Decode<DraftAttachmentReceipt>(x.ReceiptJson)).ToList();
            if (fresh.Count == 0) return new DraftAttachmentBatchResult(null, receipts.OrderBy(x => DraftCanonical.Key(x.OperationId), StringComparer.Ordinal).ToList(), true);
            if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) throw Error("DraftAcceptanceUnavailable");
            var removed = await db.ItemRemovalOperations.AsNoTracking().Where(x => x.TaskId == taskId).Select(x => x.InspectionAttemptId).ToListAsync(ct);
            if (operations.Any(x => !Current(task, x.TaskItemId, x.InspectionAttemptId)) ||
                fresh.Any(x => x.BaseDraftVersion != b.ExpectedDraftVersion || x.Kind == "AssociateAttachment" && removed.Contains(x.InspectionAttemptId))) throw Error("DraftAttemptConflict");
            var photos = await db.InspectionAttachments.AsNoTracking().Where(x => x.TaskId == taskId && attachmentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var projections = await db.DraftAttachmentProjections.Where(x => x.TaskId == taskId).ToDictionaryAsync(x => x.AttachmentId, ct);
            var claims = new List<SourceClaim>();
            foreach (var o in fresh) {
                if (o.Kind is not ("AssociateAttachment" or "DetachAttachment")) throw Error("UnsupportedAttachmentOperation", 400);
                if (!photos.TryGetValue(o.AttachmentId, out var a) || a.TaskItemId != o.TaskItemId || a.InspectionAttemptId != o.InspectionAttemptId ||
                    a.ClientPhotoId != o.ClientPhotoId || a.ExpectedSha256 != o.Sha256.ToUpperInvariant() || a.LineId != task.LineId ||
                    a.EnvironmentId != authority.EnvironmentId || a.WorkflowVersion != 2) throw Error("AttachmentScopeConflict");
                claims.Add(new(o.OperationId, o.SourceWorkContextId, o.ContributorUserIdAssertion, []));
                projections.TryGetValue(a.Id, out var projection);
                if (projection is null) {
                    if (o.Kind != "AssociateAttachment" || o.Predecessor is not null) throw Error("AttachmentPredecessorConflict");
                } else {
                    if (o.Predecessor is null || projection.LastOperationId != o.Predecessor.OperationId || projection.IsAttached == (o.Kind == "AssociateAttachment")) throw Error("AttachmentPredecessorConflict");
                    var previous = await db.DraftAttachmentOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == projection.LastOperationId, ct);
                    if (previous.ContentHash != o.Predecessor.ContentHash.ToUpperInvariant()) throw Error("AttachmentPredecessorConflict");
                }
                if (o.Kind == "AssociateAttachment") {
                    // Ready storage never exempts A from first business-incorporation checks.
                    // A previously accepted reference is the sealed frontier on explicit reattachment.
                    if (projection is null) claims.Add(new(o.OperationId, a.SourceWorkContextId, a.ContributorUserId, []));
                }
            }
            // Exact fresh source map, shared proof permitted when photo and reference share A.
            _ = await Sources(task, b.SourceProofs, claims, authority, ct);
            foreach (var id in fresh.Select(x => x.AttachmentId).Order()) {
                if (db.Database.IsNpgsql()) _ = await db.AttachmentAvailabilities.FromSqlInterpolated($"SELECT * FROM \"AttachmentAvailabilities\" WHERE \"AttachmentId\" = {id} FOR UPDATE").AsNoTracking().ToListAsync(ct);
            }
            foreach (var o in fresh.Where(x => x.Kind == "AssociateAttachment"))
                if (await ReferenceAvailability(photos[o.AttachmentId], ct) != "Ready") throw Error("AttachmentNotReady");
            if (b.ExpectedDraftVersion == long.MaxValue) throw Error("DraftVersionExhausted");
            var next = b.ExpectedDraftVersion + 1; var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            var root = await db.SharedDrafts.SingleOrDefaultAsync(x => x.TaskId == taskId, ct);
            if (root is null) { root = new SharedDraft { TaskId = taskId }; db.SharedDrafts.Add(root); await db.SaveChangesAsync(ct); }
            if (await db.SharedDrafts.Where(x => x.TaskId == taskId && x.DraftVersion == b.ExpectedDraftVersion && x.DraftVersion < long.MaxValue)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DraftVersion, x => x.DraftVersion + 1), ct) != 1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            var newRows = new List<DraftAttachmentOperation>();
            foreach (var o in fresh) {
                var a = photos[o.AttachmentId]; var storage = await db.AttachmentAcceptances.AsNoTracking().SingleAsync(x => x.AttachmentId == a.Id, ct);
                var receipt = new DraftAttachmentReceipt(taskId, o.OperationId, b.BatchId, 1, hashes[o.OperationId], o.Kind, a.TaskItemId, a.InspectionAttemptId, a.Id,
                    a.ClientPhotoId, a.ExpectedSha256, a.SourceWorkContextId, a.ContributorUserId, storage.UploadedByUserId, storage.UploadedPersonalSessionId,
                    o.SourceWorkContextId, o.ContributorUserIdAssertion, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, task.LineId!.Value, 2, now, next,
                    o.Predecessor is null ? null : new(o.Predecessor.OperationId, o.Predecessor.ContentHash.ToUpperInvariant()), o.Kind == "AssociateAttachment");
                receipts.Add(receipt);
                newRows.Add(new() { TaskId = taskId, OperationId = o.OperationId, BatchId = b.BatchId, AttachmentId = a.Id, TaskItemId = a.TaskItemId, InspectionAttemptId = a.InspectionAttemptId,
                    ClientPhotoId = a.ClientPhotoId, Sha256 = a.ExpectedSha256, PhotoSourceWorkContextId = a.SourceWorkContextId, PhotoContributorUserId = a.ContributorUserId,
                    PhotoUploadedByUserId = storage.UploadedByUserId, PhotoUploadPersonalSessionId = storage.UploadedPersonalSessionId, SourceWorkContextId = receipt.SourceWorkContextId,
                    ContributorUserId = receipt.ContributorUserId, ActorUserId = receipt.ActorUserId, ActorPersonalSessionId = receipt.ActorPersonalSessionId, EnvironmentId = authority.EnvironmentId,
                    LineId = receipt.LineId, Kind = o.Kind, ContentHash = receipt.ContentHash, CanonicalPayload = payloads[o.OperationId], ReceiptJson = Encode(receipt), AcceptedAtUtc = now,
                    DraftVersion = next, PredecessorOperationId = receipt.Predecessor?.OperationId, PredecessorHash = receipt.Predecessor?.ContentHash });
            }
            var all = receipts.OrderBy(x => DraftCanonical.Key(x.OperationId), StringComparer.Ordinal).ToList();
            var outcome = new DraftBatchReceipt(taskId, b.BatchId, fingerprint, authority.UserId, authority.PersonalSessionId, now, b.ExpectedDraftVersion, next, [], ReceiptVersion: 3, AttachmentOperations: all);
            db.AcceptedBatchReceipts.Add(new() { TaskId = taskId, BatchId = b.BatchId, DraftVersion = next, RequestFingerprint = fingerprint, ReceiptJson = Encode(outcome) });
            await db.SaveChangesAsync(ct); db.DraftAttachmentOperations.AddRange(newRows); await db.SaveChangesAsync(ct);
            foreach (var o in fresh) {
                if (!projections.TryGetValue(o.AttachmentId, out var projection)) {
                    projection = new() { TaskId = taskId, AttachmentId = o.AttachmentId }; projections.Add(o.AttachmentId, projection); db.DraftAttachmentProjections.Add(projection);
                }
                projection.LastOperationId = o.OperationId; projection.IsAttached = o.Kind == "AssociateAttachment";
            }
            // Reservation cap is also five; independently enforce the current reference projection bound.
            var activeIds = projections.Values.Where(x => x.IsAttached).Select(x => x.AttachmentId).ToArray();
            var active = await db.InspectionAttachments.AsNoTracking().Where(x => activeIds.Contains(x.Id)).Select(x => x.InspectionAttemptId).ToListAsync(ct);
            if (active.GroupBy(x => x).Any(g => g.Count() > 5)) throw Error("DraftAttachmentLimitExceeded");
            await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id = Guid.NewGuid(), TaskId = taskId, BatchId = b.BatchId, ActorUserId = authority.UserId, PersonalSessionId = authority.PersonalSessionId,
                OccurredAtUtc = now, BeforeVersion = b.ExpectedDraftVersion, AfterVersion = next, NewOperationCount = fresh.Count });
            return new DraftAttachmentBatchResult(outcome, all, false);
        }, ct));
    }
    private static void AttachmentShape(DraftAttachmentBatchInput b)
    {
        if (b.BatchId == Guid.Empty || b.ExpectedTaskLineId == Guid.Empty || b.ExpectedDraftVersion < 0 || b.ExpectedEnvironmentId is not { Length: > 0 and <= 200 } ||
            b.Operations is not { Count: >= 1 and <= 10 } || b.SourceProofs is not { Count: <= 20 } ||
            b.Operations.Any(o => o is null || o.OperationId == Guid.Empty || o.TaskItemId == Guid.Empty || o.InspectionAttemptId == Guid.Empty || o.AttachmentId == Guid.Empty ||
                o.ClientPhotoId == Guid.Empty || !DraftCanonical.IsHash(o.Sha256) || o.SourceWorkContextId == Guid.Empty || o.ContributorUserIdAssertion == Guid.Empty ||
                o.BaseDraftVersion < 0 || o.OriginLocalEditVersion < 0 || o.Predecessor is { } p && (p.OperationId == Guid.Empty || !DraftCanonical.IsHash(p.ContentHash))) ||
            b.Operations.Select(x => x.OperationId).Distinct().Count() != b.Operations.Count || b.Operations.Select(x => x.AttachmentId).Distinct().Count() != b.Operations.Count ||
            b.SourceProofs.Any(x => x is null || x.WorkContextId == Guid.Empty) || b.SourceProofs.Select(x => x.WorkContextId).Distinct().Count() != b.SourceProofs.Count) throw Error("InvalidAttachmentBatch", 400);
    }
    private async Task<string> ReferenceAvailability(InspectionAttachment a, CancellationToken ct)
    {
        var state = await db.AttachmentAvailabilities.AsNoTracking().SingleAsync(x => x.AttachmentId == a.Id, ct);
        if (state.State != "Ready") return state.State;
        if (state.AcceptedAttachmentId != a.Id || !await db.AttachmentAcceptances.AnyAsync(x => x.AttachmentId == a.Id, ct)) return "Unready";
        try {
            var bytes = await files.Read(a.StorageKey, true, ct); if (bytes is null) return "MissingUnexpected";
            await verifier.Verify(bytes, a.ExpectedBytes, a.ExpectedSha256, a.DeclaredContentType, ct); return "Ready";
        } catch (PhotoStorageException ex) when (ex.Message is "PhotoContentMismatch" or "PhotoBytesCorrupt" or "UnsupportedOrCorruptPhoto" or "PhotoTypeMismatch") { return "Corrupt"; }
    }
    public Task<DraftAttachmentHistory> ReadAttachmentsAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct, long? beforeDraftVersion = null) => store.RunAsync(actor, false, taskId, [], async _ => {
        if (beforeDraftVersion is <= 0) throw Error("InvalidHistoryCursor", 400);
        var task = await identity.Target(taskId, null, ct);
        var removed = await db.ItemRemovalOperations.AsNoTracking().Where(x => x.TaskId == taskId).Select(x => x.InspectionAttemptId).ToListAsync(ct);
        var ledger = db.DraftAttachmentOperations.AsNoTracking().Where(x => x.TaskId == taskId);
        // Page complete accepted batches, at most 100 versions / 1000 operations.
        // Current projection resolution is independent of the historical page.
        var versions = await ledger.Where(x => beforeDraftVersion == null || x.DraftVersion < beforeDraftVersion)
            .Select(x => x.DraftVersion).Distinct().OrderDescending().Take(101).ToListAsync(ct);
        var selected = versions.Take(100).ToArray();
        var history = await ledger.Where(x => selected.Contains(x.DraftVersion)).OrderBy(x => x.DraftVersion).ThenBy(x => x.OperationId).ToListAsync(ct);
        var projections = await db.DraftAttachmentProjections.AsNoTracking().Where(x => x.TaskId == taskId).OrderBy(x => x.AttachmentId).ToListAsync(ct);
        var lastIds = projections.Select(x => x.LastOperationId).ToArray();
        var last = await ledger.Where(x => lastIds.Contains(x.OperationId)).ToDictionaryAsync(x => x.OperationId, ct);
        var associations = new List<DraftAttachmentOutput>();
        foreach (var p in projections) {
            var a = await db.InspectionAttachments.AsNoTracking().SingleAsync(x => x.Id == p.AttachmentId, ct);
            associations.Add(new(a.Id, a.ClientPhotoId, a.TaskItemId, a.InspectionAttemptId, p.IsAttached, Current(task, a.TaskItemId, a.InspectionAttemptId) && !removed.Contains(a.InspectionAttemptId),
                await ReferenceAvailability(a, ct), Decode<DraftAttachmentReceipt>(last[p.LastOperationId].ReceiptJson)));
        }
        return new DraftAttachmentHistory(taskId, task.SharedDraft?.DraftVersion ?? 0, associations, history.Select(x => Decode<DraftAttachmentReceipt>(x.ReceiptJson)).ToList(),
            HistoryBeforeDraftVersion: versions.Count > 100 ? selected.Min() : null);
    }, ct);
}
