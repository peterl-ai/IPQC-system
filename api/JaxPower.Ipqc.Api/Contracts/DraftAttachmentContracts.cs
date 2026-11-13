using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record DraftAttachmentOperationInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid TaskItemId, Guid InspectionAttemptId, Guid AttachmentId, Guid ClientPhotoId, string Sha256,
    Guid SourceWorkContextId, Guid ContributorUserIdAssertion, long BaseDraftVersion, long OriginLocalEditVersion,
    DraftOperationReference? Predecessor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record DraftAttachmentBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, long ExpectedDraftVersion,
    List<DraftAttachmentOperationInput> Operations, List<DraftSourceProof> SourceProofs);

public sealed record DraftAttachmentReceipt(Guid TaskId, Guid OperationId, Guid BatchId, int CanonicalizationVersion,
    string ContentHash, string Kind, Guid TaskItemId, Guid InspectionAttemptId, Guid AttachmentId, Guid ClientPhotoId,
    string Sha256, Guid PhotoSourceWorkContextId, Guid PhotoContributorUserId, Guid PhotoUploadedByUserId,
    Guid PhotoUploadPersonalSessionId, Guid SourceWorkContextId, Guid ContributorUserId, Guid ActorUserId,
    Guid ActorPersonalSessionId, string EnvironmentId, Guid LineId, int WorkflowVersion, DateTime AcceptedAtUtc,
    long DraftVersion, DraftOperationReference? Predecessor, bool IsAttached);
public sealed record DraftAttachmentBatchResult(DraftBatchReceipt? BatchReceipt, List<DraftAttachmentReceipt> Operations,
    bool ReplayOnly, bool CanExecute = false, bool AcceptsFormalInspection = false);
public sealed record DraftAttachmentOutput(Guid AttachmentId, Guid ClientPhotoId, Guid TaskItemId, Guid InspectionAttemptId,
    bool IsAttached, bool IsCurrentApplicable, string Availability, DraftAttachmentReceipt LastOperation);
public sealed record DraftAttachmentHistory(Guid TaskId, long DraftVersion, List<DraftAttachmentOutput> Associations,
    List<DraftAttachmentReceipt> History, bool CanExecute = false, string Completeness = "NotEvaluatedForFormalSubmission",
    long? HistoryBeforeDraftVersion = null);
