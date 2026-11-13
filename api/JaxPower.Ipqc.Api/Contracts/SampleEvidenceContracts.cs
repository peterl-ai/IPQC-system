using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SampleEvidenceOperationInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid BindingId, Guid TaskItemId, Guid InspectionAttemptId, DraftOperationReference SampleOperation,
    DraftOperationReference? PhotoReference, Guid SourceWorkContextId, Guid ContributorUserIdAssertion,
    long BaseDraftVersion, long OriginLocalEditVersion, DraftOperationReference? Predecessor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record SampleEvidenceBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, long ExpectedDraftVersion,
    SampleEvidenceOperationInput Operation, DraftSourceProof? SourceProof);
public sealed record AcceptedSampleTransition(string Classification, DraftOperationReceipt Operation, DraftOperationReceipt? Predecessor);
public sealed record SampleEvidenceReceipt(Guid TaskId, Guid OperationId, Guid BatchId, Guid BindingId,
    int CanonicalizationVersion, string ContentHash, string Kind, Guid TaskItemId, Guid InspectionAttemptId,
    Guid SampleId, AcceptedSampleTransition Transition, DraftAttachmentReceipt? PhotoReference,
    Guid SourceWorkContextId, Guid ContributorUserId, Guid ActorUserId, Guid ActorPersonalSessionId,
    string EnvironmentId, Guid LineId, int WorkflowVersion, DateTime AcceptedAtUtc, long DraftVersion,
    DraftOperationReference? Predecessor, bool IsActive, bool AssociationFactOnly = true,
    bool ProvesNewEvidence = false, bool ProvesCoverage = false, bool ProvesPhysicalCaptureTime = false);
public sealed record SampleEvidenceBatchResult(DraftBatchReceipt? BatchReceipt, SampleEvidenceReceipt Operation,
    bool ReplayOnly, bool CanExecute = false, bool AcceptsFormalInspection = false);
public sealed record SampleEvidenceOutput(SampleEvidenceReceipt LastOperation, bool IsCurrentAttempt,
    bool IsCurrentSampleValue, bool IsCurrentPhotoEligible, string State);
public sealed record SampleEvidenceReadOutput(Guid TaskId, long DraftVersion, int Page, int PageSize, int TotalBindings,
    List<SampleEvidenceOutput> Bindings, List<SampleEvidenceReceipt> History, long? HistoryBeforeDraftVersion,
    bool CanExecute = false, bool AcceptsFormalInspection = false);
