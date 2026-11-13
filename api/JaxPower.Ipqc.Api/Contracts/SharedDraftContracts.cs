using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftOperationReference(Guid OperationId, string ContentHash);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftSourceProof(Guid WorkContextId, string ProofSecret);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DraftSampleInput(string? Judgment,
    [property:JsonNumberHandling(JsonNumberHandling.Strict)] decimal? InspectionValue, string? Barcode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record DraftOperationInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    Guid TaskItemId, Guid InspectionAttemptId, Guid ClientSampleId, Guid SourceWorkContextId,
    Guid ContributorUserIdAssertion, long BaseDraftVersion, long OriginLocalEditVersion,
    DraftSampleInput? Data, List<DraftOperationReference> Dependencies);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record DraftBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, long ExpectedDraftVersion,
    List<DraftOperationInput> Operations, List<DraftOperationReference> AcceptedReferences, List<DraftSourceProof> SourceProofs);

public sealed record DraftSampleValue(string InspectionType, decimal? InspectionValue, string Judgment, string? Barcode);
public sealed record DraftOperationReceipt(Guid TaskId, Guid OperationId, Guid BatchId, int CanonicalizationVersion,
    string ContentHash, string Kind, Guid TaskItemId, Guid InspectionAttemptId, Guid SampleId, Guid ClientSampleId,
    Guid SourceWorkContextId, Guid ContributorUserId, Guid UploadedByUserId, Guid UploadedPersonalSessionId,
    string EnvironmentId, Guid LineId, int WorkflowVersion, DateTime AcceptedAtUtc, long DraftVersion,
    DraftSampleValue? Value, bool IsRemoved, Guid ValueOperationId);
public sealed record DraftBatchReceipt(Guid TaskId, Guid BatchId, string RequestFingerprint,
    Guid UploadedByUserId, Guid UploadedPersonalSessionId, DateTime AcceptedAtUtc,
    long PreviousDraftVersion, long DraftVersion, List<DraftOperationReceipt> Operations);
public sealed record DraftBatchResult(DraftBatchReceipt? BatchReceipt, List<DraftOperationReceipt> Operations,
    bool ReplayOnly, bool CanExecute = false, bool AcceptsFormalInspection = false);
public sealed record DraftSampleOutput(Guid SampleId, Guid TaskItemId, Guid InspectionAttemptId, Guid ClientSampleId,
    int SequenceNo, DraftSampleValue Value, bool IsRemoved, Guid ValueOperationId,
    Guid ValueContributorUserId, Guid LastOperationId, bool IsCurrentApplicable);
public sealed record DraftAttemptScope(Guid TaskItemId, Guid InspectionAttemptId, int MinimumSampleCount);
public sealed record SharedDraftOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    string Availability, List<DraftAttemptScope> CurrentAttempts, List<DraftSampleOutput> Samples, bool CanExecute = false,
    string Completeness = "NotEvaluatedForFormalSubmission");
