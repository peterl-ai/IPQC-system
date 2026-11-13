namespace JaxPower.Ipqc.Api.Domain;

// Reservation only: no inspection, sample, draft reference or business acceptance.
public sealed class InspectionAttachment
{
    public Guid Id { get; set; }
    public Guid ClientPhotoId { get; set; }
    public Guid TaskId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid InspectionAttemptId { get; set; }
    public Guid LineId { get; set; }
    public int WorkflowVersion { get; set; } = 2;
    public string EnvironmentId { get; set; } = "";
    public Guid SourceWorkContextId { get; set; }
    public Guid ContributorUserId { get; set; }
    public long ExpectedBytes { get; set; }
    public string ExpectedSha256 { get; set; } = "";
    public string DeclaredContentType { get; set; } = "";
    public string CaptureSource { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

// Mutable physical availability is deliberately separate from immutable acceptance.
public sealed class AttachmentAvailability
{
    public Guid AttachmentId { get; set; }
    public string State { get; set; } = "AwaitingBytes";
    public long Version { get; set; }
    public Guid? ActiveUploadId { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public Guid? AcceptedAttachmentId { get; set; }
}

// Every staging key is durably registered BEFORE any file is created. No orphan cleanup here.
public sealed class AttachmentUpload
{
    public Guid Id { get; set; }
    public Guid AttachmentId { get; set; }
    public string StageKey { get; set; } = "";
    public Guid StartedByUserId { get; set; }
    public Guid StartedPersonalSessionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? StoredAtUtc { get; set; }
}

public sealed class AttachmentAcceptance
{
    public Guid AttachmentId { get; set; }
    public Guid UploadId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid UploadedPersonalSessionId { get; set; }
    public long ByteCount { get; set; }
    public string Sha256 { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string StoredEncoding { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime StoredAtUtc { get; set; }
    public DateTime ReadyAtUtc { get; set; }
}

public sealed class AttachmentEvent
{
    public Guid Id { get; set; }
    public Guid AttachmentId { get; set; }
    public string State { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public Guid PersonalSessionId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
