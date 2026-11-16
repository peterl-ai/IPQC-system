using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<PreflightRequirementState>))]
public enum PreflightRequirementState { Satisfied, Missing, Invalid, Unsupported, NotApplicable }
public sealed record SubmitPreflightRequirement(string Code, PreflightRequirementState State, List<string> Reasons);
public sealed record SubmitPreflightItem(DraftEvidenceItem Evidence, List<SubmitPreflightRequirement> Requirements,
    bool ObservationComplete, List<string> BlockingReasons);
public sealed record SubmitPreflightOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    string PlanningSnapshotStatus, string InspectionIdentityState, string TaskAvailability,
    int Page, int PageSize, int TotalItems, bool PageObservationComplete, bool TaskObservationComplete,
    bool? AllItemsRemoved, List<SubmitPreflightItem> Items, List<SubmitPreflightRequirement> TaskRequirements,
    List<string> BlockingReasons, List<string> DeferredCapabilities,
    int PreflightVersion = 1, bool CanExecute = false, bool CanSubmit = false,
    bool FormalSubmitPerformed = false, bool AcceptsFormalInspection = false,
    string Outcome = "Blocked", string Observation = "ReadOnlyPointInTime",
    string Completeness = "NotEvaluatedForFormalSubmission",
    string PhysicalEvidenceObservation = "ObservedDuringReadNotReservedForSubmit");
