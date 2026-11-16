using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using static JaxPower.Ipqc.Api.Contracts.PreflightRequirementState;

namespace JaxPower.Ipqc.Api.Services;

public sealed partial class SharedDraftService
{
    public Task<SubmitPreflightOutput> SubmitPreflightAsync(Guid taskId, ClaimsPrincipal actor, CancellationToken ct,
        int page = 1, int pageSize = 20, long? expectedDraftVersion = null, Guid? expectedTaskLineId = null,
        Guid? expectedTaskItemId = null, Guid? expectedInspectionAttemptId = null) => store.RunAsync(actor, false, taskId, [], async authority => {
        if (page < 1 || pageSize is < 1 or > 20 || (long)(page - 1) * pageSize > int.MaxValue || expectedDraftVersion < 0 ||
            expectedTaskLineId == Guid.Empty || expectedTaskItemId == Guid.Empty || expectedInspectionAttemptId == Guid.Empty ||
            expectedTaskItemId.HasValue != expectedInspectionAttemptId.HasValue) throw Error("InvalidSubmitPreflightQuery", 400);
        var task = await identity.Target(taskId, expectedTaskLineId, ct); var version = task.SharedDraft?.DraftVersion ?? 0;
        if (expectedDraftVersion is { } pinned && pinned != version) throw Error("SubmitPreflightVersionConflict");
        if (expectedTaskItemId is { } itemId) {
            var item = task.Items.SingleOrDefault(x => x.Id == itemId) ?? throw Error("NotFound", 404);
            if (!Current(task, itemId, expectedInspectionAttemptId!.Value)) throw Error("SubmitPreflightAttemptConflict");
        }
        var snapshot = PlanningSnapshot.Status(task); var initial = InitialInspectionIdentity.State(task);
        var availability = LineSharedTaskEligibility.Availability(task);
        var supported = snapshot == "ValidV1" && initial == "Initialized" && availability is "FoundationOnly" or "DraftFoundation";
        // Reuse the exact evidence calculation, bounds and physical observations, in this same Task lock.
        var assessment = await AssessEvidencePage(task, authority.EnvironmentId, ct, page, pageSize);
        var items = assessment.Items.Select(e => PreflightItem(task.Items.Single(i => i.Id == e.TaskItemId), e, supported)).ToList();
        bool? allRemoved = null;
        if (supported && task.Items.Count > 0) {
            var attempts = task.Items.Select(i => i.CurrentInspectionAttempt!.InspectionAttemptId).ToArray();
            var removals = await db.ItemRemovalOperations.AsNoTracking().Where(o => o.TaskId == taskId && attempts.Contains(o.InspectionAttemptId))
                .Select(o => new { o.TaskItemId, o.InspectionAttemptId }).Take(task.Items.Count + 1).ToListAsync(ct);
            allRemoved = task.Items.All(i => removals.Any(o => o.TaskItemId == i.Id && o.InspectionAttemptId == i.CurrentInspectionAttempt!.InspectionAttemptId));
        }
        var pageComplete = supported && items.Count > 0 && items.All(i => i.ObservationComplete);
        var taskComplete = pageComplete && page == 1 && items.Count == task.Items.Count;
        var requirements = new List<SubmitPreflightRequirement> {
            PreflightRequirement("FrozenPlanningSnapshot", snapshot == "ValidV1" ? Satisfied : snapshot == "Invalid" ? Invalid : Unsupported, "FrozenPlanningSnapshotUnavailable"),
            PreflightRequirement("CurrentInitialAttempts", initial == "Initialized" ? Satisfied : initial == "Uninitialized" ? Missing : Unsupported, "CurrentInitialAttemptUnavailable"),
            PreflightRequirement("CurrentDraftScope", supported ? Satisfied : Unsupported, "CurrentDraftScopeUnsupported"),
            PreflightRequirement("CompleteTaskObservation", taskComplete ? Satisfied : Unsupported, "TaskObservationIncomplete"),
            PreflightRequirement("FormalSubmitAcceptance", Unsupported, "FormalSubmitWorkflowNotImplemented")
        };
        if (allRemoved == true) {
            requirements.Add(PreflightRequirement("NotPerformedSubmission", Unsupported, "AllRemovedRequiresNotPerformedSubmission"));
            requirements.Add(PreflightRequirement("PqeNotPerformedConfirmation", Unsupported, "PqeNotPerformedConfirmationNotImplemented"));
        }
        // The store serializes all current writers. Also fail closed if a future writer changes the observed version.
        var finalVersion = await db.SharedDrafts.AsNoTracking().Where(x => x.TaskId == taskId).Select(x => (long?)x.DraftVersion).SingleOrDefaultAsync(ct) ?? 0;
        if (finalVersion != version) throw Error("SubmitPreflightVersionConflict");
        return new SubmitPreflightOutput(taskId, task.LineId!.Value, 2, version, snapshot, initial, availability, page, pageSize,
            task.Items.Count, pageComplete, taskComplete, allRemoved, items, requirements,
            PreflightBlockers(requirements).Concat(items.SelectMany(i => i.BlockingReasons)).Distinct().ToList(),
            ["FormalSubmitAcceptance", "PqeExactRevisionDecisions", "ReinspectionAttemptProducers", "TransitionPhotoNewnessAndCoverage",
             "F2SubmitIntentCoordination", "F3FormalEvidenceReferenceLifecycle"]);
    }, ct);

    private static SubmitPreflightRequirement PreflightRequirement(string code, PreflightRequirementState state, params string[] reasons) =>
        new(code, state, state == Satisfied ? [] : reasons.ToList());
    private static IEnumerable<string> PreflightBlockers(IEnumerable<SubmitPreflightRequirement> requirements) => requirements
        .Where(r => r.State is Missing or Invalid or Unsupported).SelectMany(r => r.Reasons);
    private static SubmitPreflightItem PreflightItem(PatrolTaskItem item, DraftEvidenceItem e, bool supported)
    {
        var definitionValid = InspectionJudgmentService.ValidateDefinition(item.InspectionType, item.SampleCount,
            item.LowerLimitOperator, item.LowerLimitValue, item.UpperLimitOperator, item.UpperLimitValue) is null;
        var exempt = e.Disposition is "Removed" or "UnableToMeasureNG";
        var requirements = new List<SubmitPreflightRequirement> {
            PreflightRequirement("FrozenMeasurementDefinition", definitionValid ? Satisfied : Invalid, "FrozenMeasurementDefinitionInvalid"),
            PreflightRequirement("CurrentSampleHistory", e.SampleHistoryComplete ? Satisfied : Unsupported, "CurrentSampleHistoryUnavailable"),
            PreflightRequirement("CurrentDisposition", e.Disposition is "Applicable" or "Removed" or "UnableToMeasureNG" ? Satisfied : Unsupported, "CurrentDispositionUnavailable")
        };
        var minimum = !supported || !e.SampleHistoryComplete || e.ActiveSampleCount is null || e.MinimumSampleCount is null ? Unsupported :
            e.MinimumSampleCount is < 1 or > 50 || e.ActiveSampleCount > 50 ? Invalid : e.ActiveSampleCount < e.MinimumSampleCount ? Missing : Satisfied;
        requirements.Add(PreflightRequirement("FrozenMinimumSamples", exempt ? NotApplicable : minimum,
            exempt ? "DispositionHasNoMeasuredSampleRequirement" : minimum == Missing ? "FrozenMinimumNotMet" : "SampleMinimumObservationUnavailable"));
        var judgment = !supported || !e.SampleHistoryComplete ? Unsupported : e.ResultState is "ObservedAllOK" or "ObservedNG" or "PartialOK" ? Satisfied : Missing;
        requirements.Add(PreflightRequirement("CurrentSampleJudgments", exempt ? NotApplicable : judgment,
            exempt ? "DispositionHasNoMeasuredSampleRequirement" : "CurrentSampleJudgmentsNotEstablished"));
        var policy = e.PhotoPolicy.HasExplicitConfirmation && e.PhotoPolicy.State == "ExplicitlyConfirmed" ? Satisfied :
            e.PhotoPolicy.State == "InvalidConfiguration" ? Invalid : Unsupported;
        requirements.Add(PreflightRequirement("FrozenOKPhotoPolicy", exempt || e.ResultState == "ObservedNG" ? NotApplicable : policy,
            exempt || e.ResultState == "ObservedNG" ? "OrdinaryOKPolicyNotApplicable" : "FrozenPhotoPolicyUnconfirmedOrInvalid"));
        var evidence = e.EvidenceStatus switch { "Satisfied" => Satisfied, "MissingEvidence" => Missing, "InvalidEvidence" => Invalid, _ => Unsupported };
        requirements.Add(PreflightRequirement("CurrentPhotoEvidence", e.Disposition == "Removed" ? NotApplicable : evidence,
            e.Disposition == "Removed" ? ["RemovedHasNoIndependentPhotoRequirement"] : e.Reasons.Concat(e.UnprovenFacts).DefaultIfEmpty("CurrentEvidenceUnsupported").ToArray()));
        requirements.Add(PreflightRequirement("AcceptedBindingObservation", e.BindingFactsComplete ? Satisfied : Unsupported, "BindingObservationUnavailable"));
        var transitionHistoryKnown = supported && e.SampleHistoryComplete;
        var unresolvedTransition = e.UnprovenFacts.Contains("NewEvidenceForEachRelevantTransition") || e.UnprovenFacts.Contains("PhysicalReinspectionOrImprovementEvent");
        requirements.Add(PreflightRequirement("TransitionPhotoNewnessAndCoverage", !transitionHistoryKnown || unresolvedTransition ? Unsupported : NotApplicable,
            !transitionHistoryKnown ? "TransitionHistoryUnavailable" : unresolvedTransition ? "TransitionPhotoNewnessAndCoverageUnsupported" : "NoTransitionSpecificRequirementObserved"));
        requirements.Add(PreflightRequirement("UnableToMeasureReason", e.Disposition == "Unavailable" ? Unsupported : e.Disposition != "UnableToMeasureNG" ? NotApplicable :
            string.IsNullOrWhiteSpace(e.UnableOperation?.NgReason) ? Missing : Satisfied,
            e.Disposition == "Unavailable" ? "UnableDispositionUnavailable" : e.Disposition != "UnableToMeasureNG" ? "UnableDispositionNotActive" : "UnableReasonMissing"));
        return new(e, requirements, supported && definitionValid && e.SampleHistoryComplete && e.BindingFactsComplete,
            PreflightBlockers(requirements).Distinct().ToList());
    }
}
