namespace JaxPower.Ipqc.Api.Domain;

// Derived uniqueness index across typed ledgers, not a second acceptance fact.
public sealed class SharedOperationIdentity
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public string Domain { get; set; } = "";
}

// Immutable per-attempt Removed fact; absence means Applicable, never OK or N/A.
public sealed class ItemRemovalOperation
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid SourceWorkContextId { get; set; }
    public Guid ContributorUserId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid UploadedPersonalSessionId { get; set; }
    public string EnvironmentId { get; set; } = "";
    public Guid LineId { get; set; }
    public int WorkflowVersion { get; set; } = 2;
    public string Kind { get; set; } = "RemoveItem";
    public int CanonicalizationVersion { get; set; } = 1;
    public string ContentHash { get; set; } = "";
    public string CanonicalPayload { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTime AcceptedAtUtc { get; set; }
    public long DraftVersion { get; set; }
}
