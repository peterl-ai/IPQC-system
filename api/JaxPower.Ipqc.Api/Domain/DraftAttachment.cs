namespace JaxPower.Ipqc.Api.Domain;

// Derived uniqueness registry; no new business facts are inferred by its migration backfill.
public sealed class DraftOperationKey
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public string Domain { get; set; } = "";
}

public sealed class DraftAttachmentOperation
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid AttachmentId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid ClientPhotoId { get; set; }
    public string Sha256 { get; set; } = "";
    public Guid PhotoSourceWorkContextId { get; set; }
    public Guid PhotoContributorUserId { get; set; }
    public Guid PhotoUploadedByUserId { get; set; }
    public Guid PhotoUploadPersonalSessionId { get; set; }
    public Guid SourceWorkContextId { get; set; }
    public Guid ContributorUserId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorPersonalSessionId { get; set; }
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
    public Guid? PredecessorOperationId { get; set; }
    public string? PredecessorHash { get; set; }
}

// Scope cannot be moved; current status is explained by an immutable accepted operation.
public sealed class DraftAttachmentProjection
{
    public Guid TaskId { get; set; }
    public Guid AttachmentId { get; set; }
    public Guid LastOperationId { get; set; }
    public bool IsAttached { get; set; }
}
