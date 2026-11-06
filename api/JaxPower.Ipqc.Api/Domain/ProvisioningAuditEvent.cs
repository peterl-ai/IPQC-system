namespace JaxPower.Ipqc.Api.Domain;

public sealed class ProvisioningAuditEvent
{
    public Guid Id { get; set; }
    public Guid ChangeId { get; set; }
    public Guid? ProductionLineId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? OldLineId { get; set; }
    public Guid? NewLineId { get; set; }
    public string Action { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public string ActorUsername { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "";
}
