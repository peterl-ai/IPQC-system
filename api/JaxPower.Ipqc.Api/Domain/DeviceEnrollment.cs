namespace JaxPower.Ipqc.Api.Domain;

public enum EnrollmentState { Invited = 1, Challenged = 2, PendingApproval = 3, Approved = 4, Issuing = 5, IssuedUnconfirmed = 6, Cancelled = 7, Expired = 8 }
public enum ChallengePurpose { Enrollment = 1, Receipt = 2 }
public enum CandidateCredentialState { PendingIssuance = 1, OutcomeUnknown = 2, IssuedUnconfirmed = 3, Revoked = 4, Failed = 5 }

public sealed class DeviceEnrollment
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid LineId { get; set; }
    public long SourceBindingVersion { get; set; }
    public long TargetBindingVersion { get; set; }
    public string Environment { get; set; } = "";
    public long Revision { get; set; } = 1;
    public EnrollmentState State { get; set; } = EnrollmentState.Invited;
    public string InvitationHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime InvitationExpiresAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? InvitationConsumedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? CsrDerBase64 { get; set; }
    public string? CsrSha256 { get; set; }
    public string? SpkiBase64 { get; set; }
    public string? SpkiSha256 { get; set; }
    public DateTime? ProofVerifiedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovalOperationId { get; set; }
    public string? ApprovalContentHash { get; set; }
    public string? TerminalReason { get; set; }
}

public sealed class EnrollmentChallenge
{
    public Guid Id { get; set; }
    public Guid EnrollmentId { get; set; }
    public ChallengePurpose Purpose { get; set; }
    public string NonceHash { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public long Revision { get; set; } = 1;
}

// A candidate is never an authenticated device. There is deliberately no Current state.
public sealed class DeviceCredential
{
    public Guid Id { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid LineId { get; set; }
    public long BindingVersion { get; set; }
    public Guid IssuanceOperationId { get; set; }
    public CandidateCredentialState State { get; set; }
    public long Revision { get; set; } = 1;
    public string IssuerId { get; set; } = "";
    public string RootCertificatePem { get; set; } = "";
    public string SpkiBase64 { get; set; } = "";
    public DateTime NotBeforeUtc { get; set; }
    public DateTime NotAfterUtc { get; set; }
    public string? CertificatePem { get; set; }
    public string? CertificateSha256 { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public bool ExternalRevocationPending { get; set; }
    public string? LastErrorCode { get; set; }
}
