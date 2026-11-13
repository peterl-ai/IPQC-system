using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    private const int EvidenceHistoryLimit = 100;
    public Task<DraftEvidenceAssessmentOutput> AssessEvidenceAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct,
        int page = 1, int pageSize = 20, long? expectedDraftVersion = null) => store.RunAsync(actor, false, taskId, [], async authority => {
        if (page < 1 || pageSize is < 1 or > 20 || (long)(page - 1) * pageSize > int.MaxValue || expectedDraftVersion < 0)
            throw Error("InvalidEvidenceAssessmentQuery", 400);
        var task = await identity.Target(taskId, null, ct); var version = task.SharedDraft?.DraftVersion ?? 0;
        if (expectedDraftVersion is { } expected && expected != version) throw Error("EvidenceAssessmentVersionConflict");
        var supported = PlanningSnapshot.Status(task) == "ValidV1" && InitialInspectionIdentity.State(task) == "Initialized" &&
            LineSharedTaskEligibility.Availability(task) is "FoundationOnly" or "DraftFoundation";
        var items = new List<DraftEvidenceItem>();
        foreach (var item in task.Items.OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize))
            items.Add(await AssessItem(task, item, supported, authority.EnvironmentId, ct));
        return new DraftEvidenceAssessmentOutput(taskId, task.LineId!.Value, 2, version, page, pageSize, task.Items.Count, items);
    }, ct);

    private async Task<DraftEvidenceItem> AssessItem(PatrolTask task, PatrolTaskItem item, bool supported, string environment, CancellationToken ct)
    {
        var attempt = item.CurrentInspectionAttempt?.InspectionAttemptId;
        var minimum = item.InspectionAttempts.SingleOrDefault(x => x.Id == attempt)?.MinimumSampleCount;
        // Peter's A/A decision: free text is not an explicit policy-confirmation record.
        var policy = new DraftEvidencePolicy(item.PhotoRequirement, item.PhotoRequirement is "Required" or "Optional" ? item.PhotoRequirement : "Unknown", "PolicyConfigurationUnresolved");
        var samples = new List<DraftEvidenceSample>(); var transitions = new List<DraftEvidenceTransition>(); var photos = new List<DraftEvidencePhoto>();
        var reasons = new List<string>(); var unproven = new List<string>(); var categories = new List<string>();
        var bindings = new List<SampleEvidenceOutput>(); var bindingsComplete = true;
        var disposition = "Applicable"; var result = "NotEstablished"; var status = "UnsupportedUntilFutureWorkflow"; var complete = true;
        UnableToMeasureReceipt? unable = null;
        DraftEvidenceItem Output() => new(item.Id, attempt, disposition, item.InspectionType, minimum,
            complete ? samples.Count(x => !x.IsRemoved) : null, complete && minimum is { } count ? samples.Count(x => !x.IsRemoved) >= count : null,
            policy, result, categories, status, reasons, unproven, complete, samples, transitions, photos, unable, bindings, bindingsComplete);
        if (!supported || attempt is null) { disposition = "Unavailable"; complete = false; reasons.Add("UnsupportedSnapshotAttemptOrFormalWorkflow"); unproven.Add("CurrentInitialDraftScope"); return Output(); }
        var aid = attempt.Value;
        var removed = await db.ItemRemovalOperations.AsNoTracking().AnyAsync(x => x.TaskId == task.Id && x.InspectionAttemptId == aid, ct);
        var up = await db.UnableToMeasureProjections.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == task.Id && x.InspectionAttemptId == aid, ct);
        if (up is not null) unable = Decode<UnableToMeasureReceipt>((await db.UnableToMeasureOperations.AsNoTracking().SingleAsync(x => x.TaskId == task.Id && x.OperationId == up.LastOperationId, ct)).ReceiptJson);
        var identities = await db.SharedSamples.AsNoTracking().Where(x => x.TaskId == task.Id && x.TaskItemId == item.Id && x.InspectionAttemptId == aid)
            .OrderBy(x => x.SequenceNo).Take(EvidenceHistoryLimit + 1).ToListAsync(ct);
        var rows = await db.ContributionOperations.AsNoTracking().Where(x => x.TaskId == task.Id && x.TaskItemId == item.Id && x.InspectionAttemptId == aid)
            .OrderBy(x => x.OperationId).Take(EvidenceHistoryLimit + 1).ToListAsync(ct);
        complete = identities.Count <= EvidenceHistoryLimit && rows.Count <= EvidenceHistoryLimit;
        if (complete) {
            var ids = rows.Select(x => x.OperationId).ToArray();
            var deps = await db.ContributionDependencies.AsNoTracking().Where(x => x.TaskId == task.Id && ids.Contains(x.OperationId)).Take(EvidenceHistoryLimit + 1).ToListAsync(ct);
            var sampleIds = identities.Select(x => x.Id).ToArray();
            var projections = await db.SharedSampleProjections.AsNoTracking().Where(x => x.TaskId == task.Id && sampleIds.Contains(x.SampleId)).ToDictionaryAsync(x => x.SampleId, ct);
            var receipts = new Dictionary<Guid, DraftOperationReceipt>();
            try { foreach (var row in rows) receipts.Add(row.OperationId, Receipt(row)); }
            catch (JsonException) { complete = false; }
            if (complete) complete = BuildEvidenceHistory(task, item, aid, identities, rows, deps, projections, receipts, samples, transitions);
        }
        var observations = new Dictionary<Guid, string>();
        photos = await EvidencePhotos(task, item.Id, aid, environment, rows, removed, ct, observations);
        var bindingRows = await (from p in db.SampleEvidenceProjections.AsNoTracking()
            join o in db.SampleEvidenceOperations.AsNoTracking() on new { p.TaskId, OperationId = p.LastOperationId } equals new { o.TaskId, o.OperationId }
            where p.TaskId == task.Id && o.TaskItemId == item.Id && o.InspectionAttemptId == aid && p.IsActive
            orderby p.BindingId select o).Take(EvidenceHistoryLimit + 1).ToListAsync(ct);
        bindingsComplete = bindingRows.Count <= EvidenceHistoryLimit;
        foreach (var row in bindingRows.Take(EvidenceHistoryLimit)) bindings.Add(await BindingObservation(task, row, true, ct, observations));
        photos = photos.Select(p => p with { BindsSampleTransition = bindings.Any(b => b.IsCurrentPhotoEligible && b.LastOperation.PhotoReference?.OperationId == p.LastReference?.OperationId) }).ToList();
        if (removed) { disposition = "Removed"; result = "Removed"; categories.Add("NoneForRemoved"); status = "Satisfied"; return Output(); }
        if (up is { IsActive: true }) {
            disposition = "UnableToMeasureNG"; result = "UnableToMeasureNG"; categories.Add("UnableReasonAndCurrentAttemptPhoto");
            var observed = unable?.Evidence is { } e ? await UnableEvidence(task, item.Id, aid, e, ct, observations) : null;
            if (string.IsNullOrWhiteSpace(unable?.NgReason)) { status = "InvalidEvidence"; reasons.Add("UnableReasonMissing"); }
            else if (observed is null) { status = "MissingEvidence"; reasons.Add("UnableAcceptedEvidenceMissing"); }
            else { status = observed.IsValid ? "Satisfied" : "InvalidEvidence"; if (!observed.IsValid) reasons.Add("UnableEvidence" + observed.State); }
            return Output();
        }
        if (!complete) { samples.Clear(); transitions.Clear(); result = "SampleHistoryUnavailable"; reasons.Add("SampleHistoryUnavailableOrAssessmentLimitExceeded"); unproven.Add("CompleteAcceptedSampleHistory"); return Output(); }
        var active = samples.Where(x => !x.IsRemoved).ToList();
        var ng = active.Any(x => x.ValueOperation?.Value?.Judgment == "NG");
        result = active.Count == 0 ? "NoApplicableSamples" : ng ? "ObservedNG" : active.Count >= minimum ? "ObservedAllOK" : "PartialOK";
        if (active.Count == 0) { reasons.Add("CurrentResultNotEstablished"); unproven.Add("ApplicableInspectionResult"); return Output(); }
        var corrections = transitions.Where(x => x.Classification is "NGToOKCorrection" or "NGToNGCorrection" or "OKToNGCorrection").ToList();
        var priorNg = transitions.Any(x => x.ToJudgment == "NG");
        var removedNg = transitions.Any(x => x.Kind == "RemoveSample" && x.FromJudgment == "NG");
        if (ng) categories.Add("CurrentAttemptNGPhoto");
        if (corrections.Any(x => x.Classification == "NGToOKCorrection")) categories.Add("ImprovementPhoto");
        if (corrections.Any(x => x.Classification == "NGToNGCorrection")) categories.Add("RepeatedNGEvidence");
        if (corrections.Any(x => x.Classification == "OKToNGCorrection")) categories.Add("NGTransitionPhoto");
        if (removedNg || !ng && priorNg && corrections.Count == 0) categories.Add("HistoricalNGResolution");
        if (up is not null) categories.Add("WithdrawnUnableResolution");
        if (corrections.Count > 0 || removedNg || !ng && priorNg || up is not null) {
            var associated = bindingsComplete && corrections.Count > 0 && corrections.All(t => bindings.Any(b => b.IsCurrentPhotoEligible && b.LastOperation.Transition.Operation.OperationId == t.OperationId));
            reasons.Add(associated ? "NewEvidenceOrCoverageSemanticsUnresolved" : "PhotoToInspectionTransitionAssociationNotRecorded");
            if (!bindingsComplete) reasons.Add("BindingAssessmentLimitExceeded");
            unproven.Add("NewEvidenceForEachRelevantTransition");
            unproven.Add("PhysicalReinspectionOrImprovementEvent"); return Output();
        }
        if (ng) {
            if (photos.Any(x => x.IsEligibleEvidence)) status = "Satisfied";
            else if (photos.Any(x => x.LastReference is not null)) { status = "InvalidEvidence"; reasons.Add("AcceptedReferencesDetachedOrUnavailable"); }
            else { status = "MissingEvidence"; reasons.Add("NoAcceptedCurrentAttemptReference"); }
            return Output();
        }
        if (active.Count < minimum) { categories.Add("ResultNotEstablished"); reasons.Add("FrozenMinimumNotMet"); unproven.Add("CompleteApplicableOKResult"); return Output(); }
        categories.Add("FrozenOKPhotoPolicy"); status = "PolicyConfigurationUnresolved";
        reasons.Add("FrozenPhotoPolicyHasNoExplicitConfirmation"); unproven.Add("ConfirmedRequiredOrOptionalPolicy"); return Output();
    }

    private static bool BuildEvidenceHistory(PatrolTask task, PatrolTaskItem item, Guid attempt, List<SharedSample> identities,
        List<ContributionOperation> rows, List<ContributionDependency> deps, Dictionary<Guid, SharedSampleProjection> projections,
        Dictionary<Guid, DraftOperationReceipt> receipts, List<DraftEvidenceSample> samples, List<DraftEvidenceTransition> transitions)
    {
        if (deps.Count > EvidenceHistoryLimit || identities.Count != projections.Count || rows.Any(x => x.CanonicalizationVersion != 1)) return false;
        var depths = new Dictionary<Guid, int>(); var visiting = new HashSet<Guid>();
        int Depth(Guid id) {
            if (depths.TryGetValue(id, out var known)) return known;
            if (!visiting.Add(id)) throw new InvalidOperationException("Cyclic accepted dependency");
            var r = receipts[id]; var ds = deps.Where(x => x.OperationId == id).ToList();
            if (r.TaskId != task.Id || r.TaskItemId != item.Id || r.InspectionAttemptId != attempt || r.OperationId != id ||
                r.Kind is not ("CaptureSample" or "CorrectSample" or "RemoveSample") || ds.Count != (r.Kind == "CaptureSample" ? 0 : 1))
                throw new InvalidOperationException("Unsupported accepted history");
            DraftOperationReceipt? previous = null; DraftOperationReference? reference = null; var depth = 0;
            if (ds.Count == 1) {
                var d = ds[0]; previous = receipts[d.DependencyId];
                if (previous.SampleId != r.SampleId || previous.ContentHash != d.ContentHash || previous.IsRemoved) throw new InvalidOperationException("Invalid accepted predecessor");
                reference = new(d.DependencyId, d.ContentHash); depth = Depth(d.DependencyId) + 1;
            }
            if (r.Kind == "RemoveSample" ? r.Value is not null || !r.IsRemoved : r.Value is null || r.IsRemoved) throw new InvalidOperationException("Invalid accepted value");
            var from = previous?.Value?.Judgment; var to = r.Value?.Judgment;
            var classification = r.Kind == "CaptureSample" ? "InitialCapture" : r.Kind == "RemoveSample" ? "SampleRemoved" :
                from == "NG" && to == "OK" ? "NGToOKCorrection" : from == "NG" && to == "NG" ? "NGToNGCorrection" :
                from == "OK" && to == "NG" ? "OKToNGCorrection" : "OtherDraftCorrection";
            transitions.Add(new(r.SampleId, id, r.Kind, classification, reference, from, to, r.DraftVersion, depth));
            visiting.Remove(id); depths.Add(id, depth); return depth;
        }
        try {
            foreach (var row in rows) _ = Depth(row.OperationId);
            foreach (var s in identities) {
                if (!projections.TryGetValue(s.Id, out var p) || !receipts.TryGetValue(p.ValueOperationId, out var value) || !receipts.TryGetValue(p.LastOperationId, out var last) ||
                    value.SampleId != s.Id || last.SampleId != s.Id || value.Value is not { } v || last.IsRemoved != p.IsRemoved ||
                    v.Judgment != p.Judgment || v.InspectionType != p.InspectionType || v.InspectionValue != p.InspectionValue || v.Barcode != p.Barcode ||
                    v.InspectionType != InspectionJudgmentService.CanonicalType(item.InspectionType) ||
                    v.InspectionType == "Quantitative" && (v.InspectionValue is not { } number || InspectionJudgmentService.QuantitativeSampleJudgment(item, number) != v.Judgment)) return false;
                samples.Add(new(s.Id, s.ClientSampleId, s.SequenceNo, p.IsRemoved, p.LastOperationId, value));
            }
        } catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { return false; }
        // Display order uses exact predecessor depth, including several changes in one atomic batch.
        transitions.Sort((a, b) => a.SampleId == b.SampleId ? a.DependencyDepth.CompareTo(b.DependencyDepth) : a.SampleId.CompareTo(b.SampleId));
        return true;
    }

    private async Task<List<DraftEvidencePhoto>> EvidencePhotos(PatrolTask task, Guid item, Guid attempt, string environment,
        List<ContributionOperation> sampleOperations, bool removed, CancellationToken ct, Dictionary<Guid, string> observations)
    {
        var objects = await db.InspectionAttachments.AsNoTracking().Where(x => x.TaskId == task.Id && x.TaskItemId == item && x.InspectionAttemptId == attempt)
            .OrderBy(x => x.Id).Take(6).ToListAsync(ct);
        if (objects.Count > 5) throw Error("EvidencePhotoScopeLimitExceeded");
        var result = new List<DraftEvidencePhoto>();
        foreach (var a in objects) {
            var p = await db.DraftAttachmentProjections.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == task.Id && x.AttachmentId == a.Id, ct);
            var first = await db.AttachmentAcceptances.AsNoTracking().SingleOrDefaultAsync(x => x.AttachmentId == a.Id, ct);
            DraftAttachmentReceipt? receipt = null;
            if (p is not null) receipt = Decode<DraftAttachmentReceipt>((await db.DraftAttachmentOperations.AsNoTracking().SingleAsync(x => x.TaskId == task.Id && x.OperationId == p.LastOperationId, ct)).ReceiptJson);
            var scope = a.LineId == task.LineId && a.EnvironmentId == environment && a.WorkflowVersion == 2 && Current(task, item, attempt) &&
                receipt is not null && receipt.TaskId == task.Id && receipt.TaskItemId == item && receipt.InspectionAttemptId == attempt && receipt.AttachmentId == a.Id &&
                receipt.EnvironmentId == environment && receipt.LineId == task.LineId && receipt.WorkflowVersion == 2 && receipt.Sha256 == a.ExpectedSha256;
            var availability = p is null ? (await db.AttachmentAvailabilities.AsNoTracking().SingleAsync(x => x.AttachmentId == a.Id, ct)).State : scope ? await BindingAvailability(a, ct, observations) : "ScopeMismatch";
            var eligible = scope && !removed && p is { IsAttached: true } && receipt!.Kind == "AssociateAttachment" && availability == "Ready";
            result.Add(new(a.Id, item, attempt, a.SourceWorkContextId, a.ContributorUserId, first?.UploadedByUserId, first?.UploadedPersonalSessionId,
                first is null ? null : DateTime.SpecifyKind(first.ReadyAtUtc, DateTimeKind.Utc), availability,
                p is null ? "NotCheckedUnassociated" : availability == "Ready" ? "Verified" : availability,
                p?.IsAttached ?? false, scope && !removed, eligible, receipt,
                receipt is null ? [] : sampleOperations.Where(x => x.DraftVersion < receipt.DraftVersion).Select(x => x.OperationId).Order().ToList()));
        }
        return result;
    }
}
