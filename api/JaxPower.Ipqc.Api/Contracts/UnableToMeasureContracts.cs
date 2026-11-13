using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record UnableToMeasureOperationInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId, Guid ContributorUserIdAssertion,
    long BaseDraftVersion, long OriginLocalEditVersion, string? NgReason,
    DraftOperationReference? Evidence, DraftOperationReference? Predecessor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record UnableToMeasureBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, long ExpectedDraftVersion,
    UnableToMeasureOperationInput Operation, DraftSourceProof? SourceProof);
public sealed record UnableToMeasureReceipt(Guid TaskId, Guid OperationId, Guid BatchId, int CanonicalizationVersion,
    string ContentHash, string Kind, Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId,
    Guid ContributorUserId, Guid ActorUserId, Guid ActorPersonalSessionId, string EnvironmentId, Guid LineId,
    int WorkflowVersion, DateTime AcceptedAtUtc, long DraftVersion, string Disposition, string? Judgment,
    string? NgReason, int MinimumSampleCount, DraftAttachmentReceipt? Evidence, DraftOperationReference? Predecessor);
public sealed record UnableToMeasureBatchResult(DraftBatchReceipt? BatchReceipt, UnableToMeasureReceipt Operation,
    bool ReplayOnly, bool CanExecute = false, bool AcceptsFormalInspection = false);
public sealed record UnableToMeasureEvidence(Guid ReferenceOperationId, Guid AttachmentId, Guid? CurrentReferenceOperationId,
    bool IsAttached, string Availability, string State, bool IsValid);
public sealed record DraftDispositionItem(Guid TaskItemId, Guid? InspectionAttemptId, int? MinimumSampleCount,
    string Disposition, string? NgReason, UnableToMeasureReceipt? LastOperation,
    UnableToMeasureEvidence? Evidence, string DraftEvidenceStatus);
public sealed record DraftDispositionOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    List<DraftDispositionItem> Items, List<UnableToMeasureReceipt> History, long? HistoryBeforeDraftVersion,
    bool CanExecute = false, string Completeness = "NotEvaluatedForFormalSubmission");
