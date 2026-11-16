namespace JaxPower.Ipqc.Api.Domain;

public sealed class PhotoPolicyDefinition
{
    public Guid Id { get; set; }
    public Guid StandardId { get; set; }
    public Guid StandardItemId { get; set; }
    public long Version { get; set; }
    public string DefinitionHash { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
public sealed class PhotoPolicyConfirmation
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid StandardId { get; set; }
    public Guid StandardItemId { get; set; }
    public Guid DefinitionId { get; set; }
    public long PolicyVersion { get; set; }
    public Guid? ExpectedConfirmationId { get; set; }
    public string Value { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public Guid ActorPersonalSessionId { get; set; }
    public string EnvironmentId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string ReceiptJson { get; set; } = "";
    public DateTime ConfirmedAtUtc { get; set; }
}
public sealed class PhotoPolicyState
{
    public Guid StandardItemId { get; set; }
    public Guid StandardId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid? ConfirmationId { get; set; }
    public long PolicyVersion { get; set; }
    public bool IsPresent { get; set; }
}
public sealed class FrozenTaskPhotoPolicy
{
    public Guid TaskItemId { get; set; }
    public Guid TaskId { get; set; }
    public Guid ConfirmationId { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
}
