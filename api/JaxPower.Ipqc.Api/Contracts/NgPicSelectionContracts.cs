using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record NgPicSelectionInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId, Guid ContributorUserIdAssertion,
    [property: JsonRequired] long BaseDraftVersion, [property: JsonRequired] long OriginLocalEditVersion,
    [property: JsonRequired] List<Guid> PicIds, DraftOperationReference? Predecessor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record NgPicBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, [property: JsonRequired] long ExpectedDraftVersion,
    NgPicSelectionInput Operation, DraftSourceProof? SourceProof);
public sealed record NgPicSnapshot(Guid PicId, long RosterRevision, string Name, string Department);
public sealed record NgPicBasis(string Kind, List<DraftOperationReference> References);
public sealed record NgPicSelectionReceipt(Guid TaskId, Guid OperationId, Guid BatchId, int CanonicalizationVersion,
    string ContentHash, string Kind, Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId,
    Guid ContributorUserId, Guid ActorUserId, Guid ActorPersonalSessionId, string EnvironmentId, Guid LineId,
    int WorkflowVersion, long BaseDraftVersion, long OriginLocalEditVersion, DateTime AcceptedAtUtc,
    long DraftVersion, DraftOperationReference? Predecessor, List<NgPicSnapshot> SelectedPics, NgPicBasis NgObservation);
public sealed record NgPicBatchResult(DraftBatchReceipt? BatchReceipt, NgPicSelectionReceipt Operation,
    bool ReplayOnly, bool CanExecute = false, bool CanSubmit = false, bool AcceptsFormalInspection = false);
public sealed record NgPicObservation(NgPicSelectionReceipt Receipt, bool IsCurrentAttempt, bool IsLatestForAttempt,
    NgPicBasis CurrentNg, bool? MatchesAcceptedNgBasis, string AssignmentDisposition = "NotEvaluatedForFormalWorkflow");
public sealed record NgPicItemOutput(Guid TaskItemId, Guid? InspectionAttemptId, NgPicBasis CurrentNg,
    bool HasAcceptedSelection, NgPicObservation? LastSelection, bool IsRequired = false, bool BlocksSubmit = false);
public sealed record NgPicReadOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    int Page, int PageSize, int TotalItems, List<NgPicItemOutput> Items, List<NgPicObservation> History,
    long? HistoryBeforeDraftVersion, bool CanExecute = false, bool CanSubmit = false,
    string Observation = "ReadOnlyPointInTime", string Completeness = "NotEvaluatedForFormalSubmission");
