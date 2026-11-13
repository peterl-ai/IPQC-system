using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record ItemRemovalOperationInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid TaskItemId, Guid InspectionAttemptId, Guid SourceWorkContextId, Guid ContributorUserIdAssertion,
    long BaseDraftVersion, long OriginLocalEditVersion, bool ConfirmedAbsentOrNotApplicable,
    string ConfirmationSource, string? Reason);

// A single explicit item, not an array/bulk removal API. No invented sample identity.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record ItemRemovalBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, long ExpectedDraftVersion,
    ItemRemovalOperationInput Operation, DraftSourceProof? SourceProof);

public sealed record ItemRemovalReceipt(Guid TaskId, Guid OperationId, Guid BatchId, string Kind,
    int CanonicalizationVersion, string ContentHash, Guid TaskItemId, Guid InspectionAttemptId,
    Guid SourceWorkContextId, Guid ContributorUserId, Guid UploadedByUserId, Guid UploadedPersonalSessionId,
    string EnvironmentId, Guid LineId, int WorkflowVersion, DateTime AcceptedAtUtc, long DraftVersion,
    string Disposition, bool ConfirmedAbsentOrNotApplicable, string ConfirmationSource, string? Reason,
    int MinimumSampleCount, int RemainingApplicableItemCount, bool WarnsLastApplicableItemRemoval);

public sealed record ItemRemovalBatchResult(DraftBatchReceipt? BatchReceipt, ItemRemovalReceipt Operation,
    bool ReplayOnly, bool CanExecute = false, bool AcceptsFormalInspection = false);

public sealed record DraftItemOutput(Guid TaskItemId, Guid InspectionAttemptId, string Disposition,
    int MinimumSampleCount, ItemRemovalReceipt? Removal);
