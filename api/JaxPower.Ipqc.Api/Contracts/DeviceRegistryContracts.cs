using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeviceCreateInput(string? DisplayName, string? Model = null, string? Serial = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeviceUpdateInput(string? DisplayName, string? Model, string? Serial, long ExpectedRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeviceBindingInput(Guid LineId, long ExpectedRevision, string? Reason = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeviceReasonInput(long ExpectedRevision, string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeviceReplaceInput(Guid ReplacementDeviceId, long ExpectedRevision, long ReplacementExpectedRevision, string? Reason);
public sealed record DeviceOutput(Guid Id, string DisplayName, string? Model, string? Serial, string Status,
    Guid? LineId, long Revision, long BindingVersion, Guid? ReplacedByDeviceId,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? TerminalAtUtc, string? TerminalReason,
    string CredentialStatus = "NotEnrolled", bool CanExecuteLineShared = false);
public sealed record ProvisioningSnapshot(string Name, long Revision, string? Code = null, bool? IsActive = null,
    string? Model = null, string? Serial = null, string? Status = null, Guid? LineId = null,
    long? BindingVersion = null, Guid? ReplacedByDeviceId = null);
public sealed record ProvisioningAuditOutput(Guid Id, Guid ChangeId, string Action, Guid ActorUserId,
    string ActorUsername, DateTime OccurredAtUtc, string? Reason, ProvisioningSnapshot? Before, ProvisioningSnapshot After,
    Guid? DeviceId);
