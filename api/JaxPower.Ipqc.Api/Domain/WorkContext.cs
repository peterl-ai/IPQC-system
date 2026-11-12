namespace JaxPower.Ipqc.Api.Domain;

public sealed class WorkContext
{
    public Guid Id { get; set; }
    public int ContractVersion { get; set; } = 1;
    public string ProofSecretHash { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid IssuedPersonalSessionId { get; set; }
    public int IssuedAuthVersion { get; set; }
    public Guid TaskId { get; set; }
    public Guid LineId { get; set; }
    public int WorkflowVersion { get; set; } = 2;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime CaptureUntilUtc { get; set; }
    public DateTime PersonalSessionExpiresAtUtc { get; set; }
    public DateTime? SecurityRevokedAtUtc { get; set; }
    public string? SecurityRevocationReason { get; set; }
    public Guid? SecurityRevokedByUserId { get; set; }
    public long Revision { get; set; } = 1;
}

public sealed class WorkContextAuditEvent
{
    public Guid Id { get; set; }
    public Guid ChangeId { get; set; }
    public Guid WorkContextId { get; set; }
    public string Action { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string ActorType { get; set; } = "User";
    public Guid ActorUserId { get; set; }
    public string ActorUsername { get; set; } = "";
    public Guid SourceUserId { get; set; }
    public string? Reason { get; set; }
    public long? BeforeRevision { get; set; }
    public long AfterRevision { get; set; }
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "";
}
