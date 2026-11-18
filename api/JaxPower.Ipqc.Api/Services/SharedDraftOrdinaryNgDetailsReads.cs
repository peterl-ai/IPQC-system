using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    private static OrdinaryNgDetailsReceipt VerifiedOrdinaryNgReceipt(OrdinaryNgDetailsOperation row, PatrolTask task, string environment)
    {
        if (row.TaskId != task.Id || row.LineId != task.LineId || row.WorkflowVersion != 2 || row.EnvironmentId != environment)
            throw Error("OrdinaryNgDetailsReceiptIntegrityUnavailable", 503);
        try
        {
            var basis = Decode<List<DraftOperationReference>>(row.NgBasisJson);
            var prior = row.PredecessorOperationId is { } id ? new DraftOperationReference(id, row.PredecessorHash!) : null;
            var expected = new OrdinaryNgDetailsReceipt(row.TaskId, row.OperationId, row.BatchId, row.CanonicalizationVersion,
                row.ContentHash, row.Kind, row.TaskItemId, row.InspectionAttemptId, row.SourceWorkContextId, row.ContributorUserId,
                row.ActorUserId, row.ActorPersonalSessionId, row.EnvironmentId, row.LineId, row.WorkflowVersion, row.BaseDraftVersion,
                row.OriginLocalEditVersion, DateTime.SpecifyKind(row.AcceptedAtUtc, DateTimeKind.Utc), row.DraftVersion,
                prior, row.NgType, row.NgReason, row.Remarks, basis);
            var input = new OrdinaryNgDetailsBatchInput(row.BatchId, 1, row.LineId, 2, row.EnvironmentId, row.BaseDraftVersion,
                new(row.OperationId, row.CanonicalizationVersion, row.Kind, row.TaskItemId, row.InspectionAttemptId, row.SourceWorkContextId,
                    row.ContributorUserId, row.BaseDraftVersion, row.OriginLocalEditVersion, row.NgType, row.NgReason, row.Remarks, prior), null);
            if (basis is not { Count: >= 1 and <= 50 } ||
                basis.Any(x => x is null || x.OperationId == Guid.Empty || !DraftCanonical.IsHash(x.ContentHash)) ||
                basis.Select(x => x.OperationId).Distinct().Count() != basis.Count ||
                Encode(expected) != row.ReceiptJson || OrdinaryNgDetailsCanonical.Operation(row.TaskId, input) != row.CanonicalPayload ||
                DraftCanonical.Hash(row.CanonicalPayload) != row.ContentHash)
                throw new JsonException();
            return expected;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            throw Error("OrdinaryNgDetailsReceiptIntegrityUnavailable", 503);
        }
    }

    public Task<OrdinaryNgDetailsReceipt> OrdinaryNgDetailsReceiptAsync(Guid taskId, Guid operationId, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async authority =>
        {
            var task = await identity.Target(taskId, null, ct);
            var row = await db.OrdinaryNgDetailsOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == operationId, ct) ?? throw Error("NotFound", 404);
            return VerifiedOrdinaryNgReceipt(row, task, authority.EnvironmentId);
        }, ct);

    public Task<OrdinaryNgDetailsReadOutput> ReadOrdinaryNgDetailsAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct,
        int page = 1, int pageSize = 20, int historyLimit = 20, long? beforeDraftVersion = null,
        long? expectedDraftVersion = null, Guid? expectedTaskLineId = null) =>
        store.RunAsync(actor, false, taskId, [], async authority =>
        {
            if (page < 1 || pageSize is < 1 or > 20 || (long)(page - 1) * pageSize > int.MaxValue ||
                historyLimit is < 1 or > 100 || beforeDraftVersion <= 0 || expectedDraftVersion < 0 || expectedTaskLineId == Guid.Empty)
                throw Error("InvalidOrdinaryNgQuery", 400);
            var task = await identity.Target(taskId, expectedTaskLineId, ct);
            var version = task.SharedDraft?.DraftVersion ?? 0;
            if (expectedDraftVersion is { } expected && expected != version) throw Error("DraftVersionConflict");

            // Both result windows share one authenticated Task observation. Cache only scopes the caller can see in either window.
            var scopes = new Dictionary<(Guid, Guid), (NgPicBasis Basis, OrdinaryNgDetailsOperation? Latest)>();
            async Task<(NgPicBasis Basis, OrdinaryNgDetailsOperation? Latest)> Scope(Guid item, Guid attempt)
            {
                if (!scopes.TryGetValue((item, attempt), out var result))
                {
                    result = (await CurrentNg(task, item, attempt, ct), await LatestOrdinaryNgDetails(taskId, item, attempt, ct));
                    scopes.Add((item, attempt), result);
                }
                return result;
            }

            OrdinaryNgDetailsObservation Observe(OrdinaryNgDetailsOperation row, NgPicBasis basis, bool isLatest)
            {
                if (row.DraftVersion > version) throw Error("OrdinaryNgDetailsReceiptIntegrityUnavailable", 503);
                var receipt = VerifiedOrdinaryNgReceipt(row, task, authority.EnvironmentId);
                var isCurrent = Current(task, row.TaskItemId, row.InspectionAttemptId);
                var hasOrdinaryNg = basis.Kind == "OrdinaryNG";
                var matches = hasOrdinaryNg && receipt.AcceptedNgBasis.SequenceEqual(basis.References);
                return new(receipt, isCurrent, isLatest, basis.Kind, hasOrdinaryNg, matches, isCurrent && isLatest && matches);
            }

            var items = new List<OrdinaryNgDetailsItem>();
            foreach (var item in task.Items.OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize))
            {
                if (item.CurrentInspectionAttempt?.InspectionAttemptId is not { } attempt)
                {
                    items.Add(new(item.Id, null, "AttemptNotCurrent", null));
                    continue;
                }
                var (basis, latest) = await Scope(item.Id, attempt);
                items.Add(new(item.Id, attempt, basis.Kind, latest is null ? null : Observe(latest, basis, true)));
            }

            var rows = await db.OrdinaryNgDetailsOperations.AsNoTracking()
                .Where(x => x.TaskId == taskId && (!beforeDraftVersion.HasValue || x.DraftVersion < beforeDraftVersion))
                .OrderByDescending(x => x.DraftVersion).Take(historyLimit + 1).ToListAsync(ct);
            var history = new List<OrdinaryNgDetailsObservation>();
            foreach (var row in rows.Take(historyLimit))
            {
                var (basis, latest) = await Scope(row.TaskItemId, row.InspectionAttemptId);
                history.Add(Observe(row, basis, latest?.OperationId == row.OperationId));
            }
            return new OrdinaryNgDetailsReadOutput(taskId, task.LineId!.Value, 2, version, page, pageSize, task.Items.Count,
                items, history, rows.Count > historyLimit ? history[^1].Receipt.DraftVersion : null);
        }, ct);
}
