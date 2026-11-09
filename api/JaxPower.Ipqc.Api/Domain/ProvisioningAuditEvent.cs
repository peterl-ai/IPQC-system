namespace JaxPower.Ipqc.Api.Domain;

public enum ProvisioningActorKind { Admin = 1, Device = 2, ApplicantKey = 3, System = 4 }
public sealed record ProvisioningActor(ProvisioningActorKind Kind, Guid? UserId, Guid? DeviceId, string Name)
{
    public static ProvisioningActor Admin(Guid id, string name) => new(ProvisioningActorKind.Admin, id, null, name);
    public static ProvisioningActor System => new(ProvisioningActorKind.System, null, null, "EnrollmentIssuer");
}

public sealed class ProvisioningAuditEvent
{
    public Guid Id { get; set; }
    public Guid ChangeId { get; set; }
    public Guid? ProductionLineId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? OldLineId { get; set; }
    public Guid? NewLineId { get; set; }
    public string Action { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public ProvisioningActorKind ActorKind { get; set; } = ProvisioningActorKind.Admin;
    public Guid? ActorDeviceId { get; set; }
    public Guid? EnrollmentId { get; set; }
    public Guid? CredentialId { get; set; }
    public string ActorUsername { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "";
}
