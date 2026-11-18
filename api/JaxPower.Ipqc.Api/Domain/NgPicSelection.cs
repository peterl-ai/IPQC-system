namespace JaxPower.Ipqc.Api.Domain;

public sealed class NgPicSelectionOperation
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
    public long BaseDraftVersion { get; set; }
    public long OriginLocalEditVersion { get; set; }
    public Guid? PredecessorOperationId { get; set; }
    public string? PredecessorHash { get; set; }
    public int PicCount { get; set; }
    public string NgKind { get; set; } = "";
    public string NgBasisJson { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string CanonicalPayload { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTime AcceptedAtUtc { get; set; }
    public long DraftVersion { get; set; }
}
public sealed class NgPicSelectionMember
{
    public Guid TaskId { get; set; }
    public Guid OperationId { get; set; }
    public Guid PicId { get; set; }
    public long RosterRevision { get; set; }
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
}
public sealed class NgPicSelectionProjection
{
    public Guid TaskId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid LastOperationId { get; set; }
}
