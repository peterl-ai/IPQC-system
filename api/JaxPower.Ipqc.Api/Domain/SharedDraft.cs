namespace JaxPower.Ipqc.Api.Domain;

public sealed class SharedDraft
{
    public Guid TaskId { get; set; }
    public long DraftVersion { get; set; }
}

// Immutable capture identity. Removal changes only the separate projection.
public sealed class SharedSample
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid ClientSampleId { get; set; }
    public int SequenceNo { get; set; }
}

public sealed class SharedSampleProjection
{
    public Guid SampleId { get; set; }
    public Guid TaskId { get; set; }
    public Guid LastOperationId { get; set; }
    public Guid ValueOperationId { get; set; }
    public string InspectionType { get; set; } = "";
    public decimal? InspectionValue { get; set; }
    public string Judgment { get; set; } = "";
    public string? Barcode { get; set; }
    public bool IsRemoved { get; set; }
}

// The immutable ledger row also durably stores the typed operation receipt.
public sealed class ContributionOperation
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid SampleId { get; set; }
    public Guid SourceWorkContextId { get; set; }
    public Guid ContributorUserId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid UploadedPersonalSessionId { get; set; }
    public string EnvironmentId { get; set; } = "";
    public Guid LineId { get; set; }
    public int WorkflowVersion { get; set; } = 2;
    public int CanonicalizationVersion { get; set; } = 1;
    public string Kind { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string CanonicalPayload { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTime AcceptedAtUtc { get; set; }
    public long DraftVersion { get; set; }
}

public sealed class ContributionDependency
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public Guid DependencyId { get; set; }
    public string ContentHash { get; set; } = "";
}

public sealed class AcceptedBatchReceipt
{
    public Guid TaskId { get; set; }
    public Guid BatchId { get; set; }
    public string RequestFingerprint { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public long DraftVersion { get; set; }
}

public sealed class SharedAcceptanceAudit
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid BatchId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid PersonalSessionId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long BeforeVersion { get; set; }
    public long AfterVersion { get; set; }
    public int NewOperationCount { get; set; }
}
