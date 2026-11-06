namespace JaxPower.Ipqc.Api.Domain;

// These are provisioning states, never authenticated device identities.
public enum DeviceProvisioningStatus { PendingCredential = 1, Revoked = 2, Replaced = 3 }

public sealed class RegisteredDevice
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public DeviceProvisioningStatus Status { get; set; } = DeviceProvisioningStatus.PendingCredential;
    public Guid? LineId { get; set; }
    public long Revision { get; set; } = 1;
    public long BindingVersion { get; set; } = 1;
    public Guid? ReplacedByDeviceId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime? TerminalAtUtc { get; set; }
    public string? TerminalReason { get; set; }
}
