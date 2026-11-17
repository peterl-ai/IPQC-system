using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    private static bool IsNg(NgPicBasis basis) => basis.Kind is "OrdinaryNG" or "UnableToMeasureNG";
    private async Task<NgPicBasis> CurrentNg(PatrolTask task, Guid item, Guid attempt, CancellationToken ct) {
        NgPicBasis State(string kind) => new(kind, []);
        if (!Current(task, item, attempt)) return State("AttemptNotCurrent");
        if (PlanningSnapshot.Status(task) != "ValidV1" || InitialInspectionIdentity.State(task) != "Initialized" ||
            LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) return State("CurrentScopeUnavailable");
        if (await db.ItemRemovalOperations.AsNoTracking().AnyAsync(x => x.TaskId == task.Id && x.TaskItemId == item && x.InspectionAttemptId == attempt, ct)) return State("Removed");
        var unable = await (from p in db.UnableToMeasureProjections.AsNoTracking() join o in db.UnableToMeasureOperations.AsNoTracking()
            on new { p.TaskId, OperationId = p.LastOperationId } equals new { o.TaskId, o.OperationId }
            where p.TaskId == task.Id && p.TaskItemId == item && p.InspectionAttemptId == attempt && p.IsActive select o).SingleOrDefaultAsync(ct);
        if (unable is not null) {
            var receipt = Decode<UnableToMeasureReceipt>(unable.ReceiptJson);
            if (receipt.TaskId != task.Id || receipt.TaskItemId != item || receipt.InspectionAttemptId != attempt || receipt.OperationId != unable.OperationId ||
                receipt.ContentHash != unable.ContentHash || receipt.Disposition != "UnableToMeasureNG" || receipt.Judgment != "NG" || string.IsNullOrWhiteSpace(receipt.NgReason))
                throw Error("NgPicBasisIntegrityUnavailable", 503);
            // Accepted disposition is NG independently of today's physical photo completeness.
            return new("UnableToMeasureNG", [new(unable.OperationId, unable.ContentHash)]);
        }
        var values = await (from s in db.SharedSamples.AsNoTracking() join p in db.SharedSampleProjections.AsNoTracking() on s.Id equals p.SampleId
            join o in db.ContributionOperations.AsNoTracking() on new { p.TaskId, OperationId = p.ValueOperationId } equals new { o.TaskId, o.OperationId }
            where s.TaskId == task.Id && s.TaskItemId == item && s.InspectionAttemptId == attempt && !p.IsRemoved
            select new { Sample = s, Projection = p, Operation = o }).Take(51).ToListAsync(ct);
        if (values.Count > 50) throw Error("NgPicBasisIntegrityUnavailable", 503);
        var refs = new List<DraftOperationReference>();
        foreach (var v in values) {
            var r = Receipt(v.Operation); var p = v.Projection;
            if (r.TaskId != task.Id || r.TaskItemId != item || r.InspectionAttemptId != attempt || r.SampleId != v.Sample.Id || r.Value is not { } data ||
                r.Kind is not ("CaptureSample" or "CorrectSample") || r.OperationId != p.ValueOperationId || r.ContentHash != v.Operation.ContentHash ||
                data.Judgment != p.Judgment || data.InspectionType != p.InspectionType || data.InspectionValue != p.InspectionValue || data.Barcode != p.Barcode)
                throw Error("NgPicBasisIntegrityUnavailable", 503);
            if (data.Judgment == "NG") refs.Add(new(r.OperationId, r.ContentHash));
        }
        return new(refs.Count > 0 ? "OrdinaryNG" : "NotCurrentlyNG", refs.OrderBy(x => DraftCanonical.Key(x.OperationId), StringComparer.Ordinal).ToList());
    }
    private async Task<NgPicSelectionReceipt> NgPicReceipt(NgPicSelectionOperation row, PatrolTask task, string environment, CancellationToken ct) {
        if (row.TaskId != task.Id || row.LineId != task.LineId || row.WorkflowVersion != 2 || row.EnvironmentId != environment) throw Error("NgPicReceiptIntegrityUnavailable", 503);
        try {
            var members = (await db.NgPicSelectionMembers.AsNoTracking().Where(x => x.TaskId == row.TaskId && x.OperationId == row.OperationId).ToListAsync(ct))
                .OrderBy(x => DraftCanonical.Key(x.PicId), StringComparer.Ordinal).Select(x => new NgPicSnapshot(x.PicId, x.RosterRevision, x.Name, x.Department)).ToList();
            var basis = Decode<NgPicBasis>(row.NgBasisJson);
            var previous = row.PredecessorOperationId is { } id ? new DraftOperationReference(id, row.PredecessorHash!) : null;
            var expected = new NgPicSelectionReceipt(row.TaskId, row.OperationId, row.BatchId, row.CanonicalizationVersion, row.ContentHash, row.Kind,
                row.TaskItemId, row.InspectionAttemptId, row.SourceWorkContextId, row.ContributorUserId, row.ActorUserId, row.ActorPersonalSessionId,
                row.EnvironmentId, row.LineId, row.WorkflowVersion, row.BaseDraftVersion, row.OriginLocalEditVersion,
                DateTime.SpecifyKind(row.AcceptedAtUtc, DateTimeKind.Utc), row.DraftVersion, previous, members, basis);
            var input = new NgPicBatchInput(row.BatchId, 1, row.LineId, 2, row.EnvironmentId, row.BaseDraftVersion,
                new(row.OperationId, row.CanonicalizationVersion, row.Kind, row.TaskItemId, row.InspectionAttemptId, row.SourceWorkContextId,
                    row.ContributorUserId, row.BaseDraftVersion, row.OriginLocalEditVersion, members.Select(x => x.PicId).ToList(), previous), null);
            if (members.Count != row.PicCount || !IsNg(basis) || basis.Kind != row.NgKind || basis.References is not { Count: >= 1 and <= 50 } ||
                Encode(expected) != row.ReceiptJson || NgPicSelectionCanonical.Operation(row.TaskId, input) != row.CanonicalPayload ||
                DraftCanonical.Hash(row.CanonicalPayload) != row.ContentHash) throw new JsonException();
            return expected;
        } catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or NullReferenceException) {
            throw Error("NgPicReceiptIntegrityUnavailable", 503);
        }
    }
    public Task<NgPicSelectionReceipt> NgPicReceiptAsync(Guid taskId, Guid operationId, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async authority => {
            var task = await identity.Target(taskId, null, ct);
            var row = await db.NgPicSelectionOperations.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == operationId, ct) ?? throw Error("NotFound", 404);
            return await NgPicReceipt(row, task, authority.EnvironmentId, ct);
        }, ct);
    public Task<NgPicReadOutput> ReadNgPicsAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct, int page = 1, int pageSize = 20,
        int historyLimit = 20, long? beforeDraftVersion = null, long? expectedDraftVersion = null, Guid? expectedTaskLineId = null) =>
        store.RunAsync(actor, false, taskId, [], async authority => {
            if (page < 1 || pageSize is < 1 or > 20 || (long)(page - 1) * pageSize > int.MaxValue || historyLimit is < 1 or > 100 || beforeDraftVersion <= 0 || expectedDraftVersion < 0 || expectedTaskLineId == Guid.Empty)
                throw Error("InvalidNgPicQuery", 400);
            var task = await identity.Target(taskId, expectedTaskLineId, ct); var version = task.SharedDraft?.DraftVersion ?? 0;
            if (expectedDraftVersion is { } expected && expected != version) throw Error("NgPicVersionConflict");
            var projections = await db.NgPicSelectionProjections.AsNoTracking().Where(x => x.TaskId == taskId).ToListAsync(ct);
            var basisCache = new Dictionary<(Guid, Guid), NgPicBasis>();
            async Task<NgPicBasis> Basis(Guid item, Guid attempt) {
                if (!basisCache.TryGetValue((item, attempt), out var basis)) basisCache[(item, attempt)] = basis = await CurrentNg(task, item, attempt, ct);
                return basis;
            }
            async Task<NgPicObservation> Observe(NgPicSelectionOperation row) {
                if (row.DraftVersion > version) throw Error("NgPicReceiptIntegrityUnavailable", 503);
                var r = await NgPicReceipt(row, task, authority.EnvironmentId, ct); var b = await Basis(row.TaskItemId, row.InspectionAttemptId);
                return new(r, Current(task, row.TaskItemId, row.InspectionAttemptId), projections.Any(p => p.LastOperationId == row.OperationId), b,
                    IsNg(b) ? Encode(b) == Encode(r.NgObservation) : null);
            }
            var items = new List<NgPicItemOutput>();
            foreach (var item in task.Items.OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)) {
                var aid = item.CurrentInspectionAttempt?.InspectionAttemptId;
                var p = projections.SingleOrDefault(p => p.TaskItemId == item.Id && p.InspectionAttemptId == aid);
                var last = p is null ? null : await db.NgPicSelectionOperations.AsNoTracking().SingleAsync(x => x.TaskId == taskId && x.OperationId == p.LastOperationId, ct);
                items.Add(new(item.Id, aid, aid is { } a ? await Basis(item.Id, a) : new("AttemptNotCurrent", []), last is not null, last is null ? null : await Observe(last)));
            }
            var rows = await db.NgPicSelectionOperations.AsNoTracking().Where(x => x.TaskId == taskId && (!beforeDraftVersion.HasValue || x.DraftVersion < beforeDraftVersion))
                .OrderByDescending(x => x.DraftVersion).Take(historyLimit + 1).ToListAsync(ct);
            var history = new List<NgPicObservation>(); foreach (var row in rows.Take(historyLimit)) history.Add(await Observe(row));
            return new NgPicReadOutput(taskId, task.LineId!.Value, 2, version, page, pageSize, task.Items.Count, items, history,
                rows.Count > historyLimit ? history[^1].Receipt.DraftVersion : null);
        }, ct);
}
