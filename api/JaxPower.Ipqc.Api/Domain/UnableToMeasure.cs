namespace JaxPower.Ipqc.Api.Domain;

// Derived universal exclusion key. Domain describes the ledger, never an authorization grant.
public sealed class AcceptedOperationIdentity
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public string Domain { get; set; } = "";
}
public sealed class UnableToMeasureOperation
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid SourceWorkContextId { get; set; }
    public Guid ContributorUserId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorPersonalSessionId { get; set; }
    public string EnvironmentId { get; set; } = "";
    public Guid LineId { get; set; }
    public int WorkflowVersion { get; set; } = 2;
    public int CanonicalizationVersion { get; set; } = 1;
    public string Kind { get; set; } = "";
    public string? NgReason { get; set; }
    public Guid? EvidenceOperationId { get; set; }
    public Guid? AttachmentId { get; set; }
    public string? EvidenceHash { get; set; }
    public Guid? PredecessorOperationId { get; set; }
    public string? PredecessorHash { get; set; }
    public string ContentHash { get; set; } = "";
    public string CanonicalPayload { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTime AcceptedAtUtc { get; set; }
    public long DraftVersion { get; set; }
}
public sealed class UnableToMeasureProjection
{
    public Guid TaskId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid LastOperationId { get; set; }
    public bool IsActive { get; set; }
}
