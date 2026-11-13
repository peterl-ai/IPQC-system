namespace JaxPower.Ipqc.Api.Contracts;

public sealed record DraftEvidencePolicy(string FrozenText, string RecognizedText, string State,
    bool HasExplicitConfirmation = false);
public sealed record DraftEvidenceSample(Guid SampleId, Guid ClientSampleId, int SequenceNo, bool IsRemoved,
    Guid LastOperationId, DraftOperationReceipt? ValueOperation);
public sealed record DraftEvidenceTransition(Guid SampleId, Guid OperationId, string Kind, string Classification,
    DraftOperationReference? Predecessor, string? FromJudgment, string? ToJudgment,
    long DraftVersion, int DependencyDepth, bool IsFormalEvent = false);
public sealed record DraftEvidencePhoto(Guid AttachmentId, Guid TaskItemId, Guid InspectionAttemptId,
    Guid PhotoSourceWorkContextId, Guid PhotoContributorUserId, Guid? PhotoUploadedByUserId,
    Guid? PhotoUploadPersonalSessionId, DateTime? ReadyAtUtc, string Availability, string Integrity,
    bool IsAttached, bool IsCurrentApplicable, bool IsEligibleEvidence, DraftAttachmentReceipt? LastReference,
    List<Guid> ReferenceAcceptedAfterOperationIds, bool BindsSampleTransition = false);
public sealed record DraftEvidenceItem(Guid TaskItemId, Guid? InspectionAttemptId, string Disposition,
    string InspectionType, int? MinimumSampleCount, int? ActiveSampleCount, bool? MeetsMinimum,
    DraftEvidencePolicy PhotoPolicy, string ResultState, List<string> RequiredEvidenceCategories,
    string EvidenceStatus, List<string> Reasons, List<string> UnprovenFacts, bool SampleHistoryComplete,
    List<DraftEvidenceSample> Samples, List<DraftEvidenceTransition> Transitions, List<DraftEvidencePhoto> Photos,
    UnableToMeasureReceipt? UnableOperation = null, List<SampleEvidenceOutput>? AcceptedBindings = null,
    bool BindingFactsComplete = false);
public sealed record DraftEvidenceAssessmentOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    int Page, int PageSize, int TotalItems, List<DraftEvidenceItem> Items, int AssessmentVersion = 2,
    bool CanExecute = false, bool AcceptsFormalInspection = false,
    string Completeness = "NotEvaluatedForFormalSubmission");
