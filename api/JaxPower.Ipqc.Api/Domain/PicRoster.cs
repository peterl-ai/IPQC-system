namespace JaxPower.Ipqc.Api.Domain;

// Independent reference identity, never an ApplicationUser or a task assignment.
public sealed class PicRosterEntry
{
    public Guid PicId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
public sealed class PicRosterRevision
{
    public Guid PicId { get; set; }
    public long Revision { get; set; }
    public Guid RequestId { get; set; }
    public int ContractVersion { get; set; } = 1;
    public string RequestHash { get; set; } = "";
    public string Action { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public bool IsActive { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorPersonalSessionId { get; set; }
    public string ActorUsername { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}
