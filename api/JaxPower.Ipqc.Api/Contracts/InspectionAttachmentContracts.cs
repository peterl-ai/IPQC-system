using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttachmentInput(Guid ClientPhotoId, int ContractVersion, Guid TaskItemId, Guid InspectionAttemptId,
    Guid ExpectedTaskLineId, int ExpectedWorkflowVersion, string ExpectedEnvironmentId, Guid SourceWorkContextId,
    Guid ContributorUserIdAssertion, string ProofSecret, long ByteCount, string Sha256, string ContentType, string CaptureSource);

public sealed record AttachmentOutput(Guid AttachmentId, Guid ClientPhotoId, Guid TaskId, Guid TaskItemId,
    Guid InspectionAttemptId, Guid LineId, int WorkflowVersion, string EnvironmentId, Guid SourceWorkContextId,
    Guid ContributorUserId, string CaptureSource, DateTime CreatedAtUtc, string Availability, bool IsReady,
    Guid? UploadedByUserId, Guid? UploadedPersonalSessionId, long? ByteCount, string? Sha256,
    string? ContentType, string? StoredEncoding, int? Width, int? Height, DateTime? StoredAtUtc, DateTime? ReadyAtUtc);
