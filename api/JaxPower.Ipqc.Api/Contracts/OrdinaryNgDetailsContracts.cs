using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record OrdinaryNgDetailsInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId, Guid ContributorUserIdAssertion,
    [property: JsonRequired] long BaseDraftVersion, [property: JsonRequired] long OriginLocalEditVersion,
    string? NgType, string? NgReason, string? Remarks, DraftOperationReference? Predecessor);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record OrdinaryNgDetailsBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, [property: JsonRequired] long ExpectedDraftVersion,
    OrdinaryNgDetailsInput Operation, DraftSourceProof? SourceProof);

public sealed record OrdinaryNgDetailsReceipt(Guid TaskId, Guid OperationId, Guid BatchId, int CanonicalizationVersion,
    string ContentHash, string Kind, Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId,
    Guid ContributorUserId, Guid ActorUserId, Guid ActorPersonalSessionId, string EnvironmentId, Guid LineId,
    int WorkflowVersion, long BaseDraftVersion, long OriginLocalEditVersion, DateTime AcceptedAtUtc, long DraftVersion,
    DraftOperationReference? Predecessor, string? NgType, string? NgReason, string? Remarks,
    List<DraftOperationReference> AcceptedNgBasis);

public sealed record OrdinaryNgDetailsBatchResult(DraftBatchReceipt? BatchReceipt, OrdinaryNgDetailsReceipt Operation,
    bool ReplayOnly, bool CanExecute = false, bool CanSubmit = false, bool AcceptsFormalInspection = false);

public sealed record OrdinaryNgDetailsObservation(OrdinaryNgDetailsReceipt Receipt, bool IsCurrentAttempt,
    bool IsLatestForAttempt, string CurrentState, bool HasCurrentOrdinaryNg, bool MatchesAcceptedNgBasis, bool IsCurrentlyApplicable);

public sealed record OrdinaryNgDetailsItem(Guid TaskItemId, Guid? InspectionAttemptId, string CurrentState,
    OrdinaryNgDetailsObservation? Latest, bool IsRequired = false, bool BlocksSubmit = false);

public sealed record OrdinaryNgDetailsReadOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    int Page, int PageSize, int TotalItems, List<OrdinaryNgDetailsItem> Items, List<OrdinaryNgDetailsObservation> History,
    long? HistoryBeforeDraftVersion, bool CanExecute = false, bool CanSubmit = false, bool AcceptsFormalInspection = false,
    string Observation = "ReadOnlyPointInTime");
