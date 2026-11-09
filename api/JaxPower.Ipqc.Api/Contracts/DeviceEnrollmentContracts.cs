using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnrollmentCreateInput(long ExpectedDeviceRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnrollmentApproveInput(long ExpectedRevision, long ExpectedDeviceRevision, Guid LineId,
    long BindingVersion, string CsrSha256, string SpkiSha256, Guid OperationId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnrollmentCancelInput(long ExpectedRevision, string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnrollmentChallengeInput(string InvitationSecret, string CsrDerBase64);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnrollmentProofInput(Guid ChallengeId, string Jws);

public sealed record EnrollmentOutput(Guid Id, Guid DeviceId, Guid LineId, long SourceBindingVersion,
    long TargetBindingVersion, string Environment, long Revision, string State, DateTime ExpiresAtUtc,
    string? CsrSha256, string? SpkiSha256, Guid? ApprovedByUserId, DateTime? ApprovedAtUtc,
    string? TerminalReason, CandidateOutput? Candidate, bool CanExecuteLineShared = false);
public sealed record EnrollmentInvitation(EnrollmentOutput Enrollment, string InvitationSecret, DateTime InvitationExpiresAtUtc);
public sealed record EnrollmentChallengeOutput(Guid ChallengeId, string ProtectedHeader, string Payload, DateTime ExpiresAtUtc);
public sealed record CandidateOutput(Guid Id, Guid IssuanceOperationId, string State, long BindingVersion,
    string? CertificateSha256, string? CertificatePem, string RootCertificatePem, DateTime NotBeforeUtc,
    DateTime NotAfterUtc, bool ExternalRevocationPending, string? LastErrorCode, bool CanExecuteLineShared = false);
