using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkContextIssueInput(Guid ExpectedTaskLineId, int ExpectedWorkflowVersion, string ExpectedEnvironmentId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkContextSource(Guid WorkContextId, string ProofSecret, Guid ContributorUserIdAssertion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkContextPreflightInput(Guid ExpectedTaskLineId, int ExpectedWorkflowVersion, string ExpectedEnvironmentId, List<WorkContextSource> Sources);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkContextRevokeInput(long ExpectedRevision, string Reason);
public sealed record WorkContextMetadata(Guid Id, int ContractVersion, string EnvironmentId, Guid UserId,
    Guid IssuedPersonalSessionId, int IssuedAuthVersion, Guid TaskId, Guid LineId, int WorkflowVersion,
    DateTime IssuedAtUtc, DateTime CaptureUntilUtc, DateTime PersonalSessionExpiresAtUtc,
    DateTime? SecurityRevokedAtUtc, string? SecurityRevocationReason, Guid? SecurityRevokedByUserId, long Revision);
public sealed record WorkContextIssued(WorkContextMetadata Context, string ProofSecret, OfflineAccessReceipt OfflineAccessReceipt, bool CanExecute = false);
public sealed record WorkContextValidated(Guid WorkContextId, Guid ContributorUserId, DateTime CaptureUntilUtc);
public sealed record WorkContextPreflight(DateTime ValidatedAtUtc, List<WorkContextValidated> Sources,
    string Purpose = "SourcePreflightOnly", bool AcceptsWork = false);
public sealed record WorkContextAuditMetadata(Guid Id, Guid ChangeId, Guid WorkContextId, string Action,
    DateTime OccurredAtUtc, string ActorType, Guid ActorUserId, string ActorUsername, Guid SourceUserId,
    string? Reason, long? BeforeRevision, long AfterRevision, WorkContextMetadata? BeforeSnapshot, WorkContextMetadata AfterSnapshot);
