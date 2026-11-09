using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class DeviceEnrollmentService(IpqcDbContext db, ProvisioningStore store, DeviceEnrollmentLifecycle lifecycle,
    DeviceEnrollmentOptions options, IDeviceCertificateIssuer issuer, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static CandidateOutput Candidate(DeviceCredential k) => new(k.Id, k.IssuanceOperationId, k.State.ToString(), k.BindingVersion,
        k.CertificateSha256, k.CertificatePem, k.RootCertificatePem, Utc(k.NotBeforeUtc), Utc(k.NotAfterUtc), k.ExternalRevocationPending, k.LastErrorCode);
    public EnrollmentOutput Output(DeviceEnrollment e, DeviceCredential? k) => new(e.Id, e.DeviceId, e.LineId, e.SourceBindingVersion,
        e.TargetBindingVersion, e.Environment, e.Revision, (e.ExpiresAtUtc <= Now && (int)e.State <= 6 ? EnrollmentState.Expired : e.State).ToString(),
        Utc(e.ExpiresAtUtc), e.CsrSha256, e.SpkiSha256, e.ApprovedByUserId, e.ApprovedAtUtc is {} time ? Utc(time) : null, e.TerminalReason, k is null ? null : Candidate(k));
    public async Task<ProvisioningPage<EnrollmentOutput>> ListAsync(Guid device, int page, int size, CancellationToken ct)
    {
        if (!await db.RegisteredDevices.AnyAsync(x => x.Id == device, ct)) throw new ProvisioningException("NotFound", 404);
        var query = db.DeviceEnrollments.AsNoTracking().Where(x => x.DeviceId == device);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        var credentials = await db.DeviceCredentials.AsNoTracking().Where(x => ids.Contains(x.EnrollmentId)).ToDictionaryAsync(x => x.EnrollmentId, ct);
        return new(rows.Select(x => Output(x, credentials.GetValueOrDefault(x.Id))).ToList(), page, size, count);
    }
    public Task<EnrollmentInvitation> CreateAsync(Guid device, EnrollmentCreateInput input, Guid actor, string name, CancellationToken ct) => store.WriteAsync(async () =>
    {
        await AdminAsync(actor, ct);
        if (string.IsNullOrWhiteSpace(options.EnvironmentId)) throw new ProvisioningException("EnrollmentUnavailable", 503);
        var d = await DeviceAsync(device, ct); ProvisioningStore.Revision(d.Revision, input.ExpectedDeviceRevision);
        if (d.LineId is not {} line) throw new ProvisioningException("InvalidTransition");
        await ActiveLineAsync(line, ct);
        var open = await db.DeviceEnrollments.Where(x => x.DeviceId == device && (int)x.State <= 6).ToListAsync(ct);
        foreach (var old in open)
        {
            if (old.ExpiresAtUtc > Now) throw new ProvisioningException("EnrollmentOpen");
            await lifecycle.CloseAsync(old, d, EnrollmentState.Expired, "DeadlineExpired", ProvisioningActor.System, Guid.NewGuid(), ct);
        }
        // Release the unique open-request slot; outer transaction includes all following writes.
        if (open.Count > 0) await db.SaveChangesAsync(ct);
        var secret = DeviceEnrollmentProof.Encode(RandomNumberGenerator.GetBytes(32));
        var e = new DeviceEnrollment { Id = Guid.NewGuid(), DeviceId = d.Id, LineId = line, SourceBindingVersion = d.BindingVersion,
            TargetBindingVersion = checked(d.BindingVersion + 1), Environment = options.EnvironmentId, CreatedByUserId = actor,
            CreatedAtUtc = Now, InvitationExpiresAtUtc = Now.AddMinutes(10), ExpiresAtUtc = Now.AddMinutes(30), InvitationHash = DeviceEnrollmentProof.Hash(secret) };
        db.DeviceEnrollments.Add(e);
        lifecycle.Audit(e, d, "EnrollmentInvited", ProvisioningActor.Admin(actor, name), null);
        return new EnrollmentInvitation(Output(e, null), secret, Utc(e.InvitationExpiresAtUtc));
    }, ct);

    public Task<EnrollmentChallengeOutput> ChallengeAsync(Guid id, EnrollmentChallengeInput input, CancellationToken ct) => store.WriteAsync(async () =>
    {
        var e = await RequestAsync(id, ct); var d = await BindingAsync(e, ct);
        if (input.InvitationSecret is null || input.InvitationSecret.Length > 128 || !DeviceEnrollmentProof.MatchesHash(input.InvitationSecret, e.InvitationHash))
            throw new ProvisioningException("InvalidInvitation", 401);
        if (e.InvitationConsumedAtUtc is not null || e.State is not (EnrollmentState.Invited or EnrollmentState.Challenged)) throw new ProvisioningException("ChallengeConsumed");
        if (e.InvitationExpiresAtUtc <= Now || e.ExpiresAtUtc <= Now) throw new ProvisioningException("EnrollmentExpired");
        var csr = DeviceEnrollmentProof.ReadCsr(input.CsrDerBase64, e.DeviceId);
        if (e.CsrSha256 is not null && e.CsrSha256 != csr.CsrHash) throw new ProvisioningException("KeyMismatch");
        await ChallengeBudget(e.Id, ct);
        foreach (var old in await db.EnrollmentChallenges.Where(x => x.EnrollmentId == e.Id && x.Purpose == ChallengePurpose.Enrollment && x.ConsumedAtUtc == null).ToListAsync(ct))
        { old.ConsumedAtUtc = Now; old.Revision++; }
        var before = DeviceEnrollmentLifecycle.Snapshot(e, d);
        e.CsrDerBase64 = csr.Csr; e.CsrSha256 = csr.CsrHash; e.SpkiBase64 = csr.Spki; e.SpkiSha256 = csr.SpkiHash;
        e.State = EnrollmentState.Challenged; e.Revision++;
        var result = NewChallenge(e, ChallengePurpose.Enrollment, e.InvitationExpiresAtUtc);
        lifecycle.Audit(e, d, "EnrollmentChallenged", ProvisioningActor.System, before);
        return result;
    }, ct);

    public Task<EnrollmentOutput> ProofAsync(Guid id, EnrollmentProofInput input, CancellationToken ct) => store.WriteAsync(async () =>
    {
        var e = await RequestAsync(id, ct); var d = await BindingAsync(e, ct);
        if (e.State != EnrollmentState.Challenged || e.InvitationConsumedAtUtc is not null) throw new ProvisioningException("ChallengeConsumed");
        if (e.InvitationExpiresAtUtc <= Now || e.ExpiresAtUtc <= Now) throw new ProvisioningException("EnrollmentExpired");
        var before = DeviceEnrollmentLifecycle.Snapshot(e, d);
        await ConsumeAsync(e, input, ChallengePurpose.Enrollment, ct);
        e.InvitationConsumedAtUtc = Now; e.ProofVerifiedAtUtc = Now; e.State = EnrollmentState.PendingApproval; e.Revision++;
        lifecycle.Audit(e, d, "EnrollmentProofVerified", new(ProvisioningActorKind.ApplicantKey, null, e.DeviceId, "ApplicantKey:" + e.SpkiSha256), before);
        return Output(e, null);
    }, ct);

    public Task<EnrollmentChallengeOutput> ReceiptChallengeAsync(Guid id, CancellationToken ct) => store.WriteAsync(async () =>
    {
        var e = await RequestAsync(id, ct);
        if (e.ProofVerifiedAtUtc is null) throw new ProvisioningException("NotFound", 404);
        await ChallengeBudget(e.Id, ct);
        // Receipt challenges do not invalidate other pending receipts and cannot authorize enrollment.
        return NewChallenge(e, ChallengePurpose.Receipt, Now.AddMinutes(2));
    }, ct);
    public Task<EnrollmentOutput> ReceiptAsync(Guid id, EnrollmentProofInput input, CancellationToken ct) => store.WriteAsync(async () =>
    {
        var e = await RequestAsync(id, ct);
        if (e.ProofVerifiedAtUtc is null) throw new ProvisioningException("InvalidProof", 400);
        await ConsumeAsync(e, input, ChallengePurpose.Receipt, ct);
        return Output(e, await db.DeviceCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == id, ct));
    }, ct);

    public Task<EnrollmentOutput> ApproveAsync(Guid id, EnrollmentApproveInput input, Guid actor, string name, CancellationToken ct) => store.WriteAsync(async () =>
    {
        await AdminAsync(actor, ct);
        var e = await RequestAsync(id, ct); var d = await BindingAsync(e, ct);
        var contentHash = DeviceEnrollmentProof.Hash(JsonSerializer.Serialize(new { input.LineId, input.BindingVersion, input.CsrSha256, input.SpkiSha256 }));
        if (input.OperationId == Guid.Empty) throw new ProvisioningException("InvalidInput", 400);
        if (e.ApprovalOperationId == input.OperationId && e.ApprovalContentHash == contentHash && e.ApprovedByUserId == actor && (int)e.State is >= 4 and <= 6)
            return Output(e, await db.DeviceCredentials.SingleAsync(x => x.EnrollmentId == id, ct));
        ProvisioningStore.Revision(e.Revision, input.ExpectedRevision); ProvisioningStore.Revision(d.Revision, input.ExpectedDeviceRevision);
        if (e.State != EnrollmentState.PendingApproval || e.ExpiresAtUtc <= Now) throw new ProvisioningException("InvalidTransition");
        if (input.LineId != e.LineId || input.BindingVersion != e.SourceBindingVersion || input.CsrSha256 != e.CsrSha256 || input.SpkiSha256 != e.SpkiSha256)
            throw new ProvisioningException("KeyMismatch");
        if (!issuer.Available) throw new ProvisioningException("IssuerUnavailable", 503);
        var profile = issuer.Profile;
        var before = DeviceEnrollmentLifecycle.Snapshot(e, d);
        e.State = EnrollmentState.Approved; e.ApprovedByUserId = actor; e.ApprovedAtUtc = Now;
        e.ApprovalOperationId = input.OperationId; e.ApprovalContentHash = contentHash; e.Revision++;
        var k = new DeviceCredential { Id = Guid.NewGuid(), DeviceId = e.DeviceId, LineId = e.LineId, EnrollmentId = e.Id,
            BindingVersion = e.TargetBindingVersion, IssuanceOperationId = Guid.NewGuid(), State = CandidateCredentialState.PendingIssuance,
            SpkiBase64 = e.SpkiBase64!, IssuerId = profile.Id, RootCertificatePem = profile.RootCertificatePem,
            NotBeforeUtc = Now.AddMinutes(-1), NotAfterUtc = new[] { Now.AddDays(90), profile.NotAfterUtc }.Min() };
        if (k.NotAfterUtc <= Now.AddMinutes(30)) throw new ProvisioningException("IssuerUnavailable", 503);
        db.DeviceCredentials.Add(k);
        lifecycle.Audit(e, d, "EnrollmentApproved", ProvisioningActor.Admin(actor, name), before, k);
        return Output(e, k);
    }, ct);

    public Task<EnrollmentOutput> CancelAsync(Guid id, EnrollmentCancelInput input, Guid actor, string name, CancellationToken ct) => store.WriteAsync(async () =>
    {
        await AdminAsync(actor, ct);
        var e = await RequestAsync(id, ct); ProvisioningStore.Revision(e.Revision, input.ExpectedRevision);
        if ((int)e.State > 6) throw new ProvisioningException("InvalidTransition");
        var d = await db.RegisteredDevices.SingleAsync(x => x.Id == e.DeviceId, ct);
        await lifecycle.CloseAsync(e, d, EnrollmentState.Cancelled, ProvisioningStore.Text(input.Reason, 2000), ProvisioningActor.Admin(actor, name), Guid.NewGuid(), ct);
        return Output(e, await db.DeviceCredentials.SingleOrDefaultAsync(x => x.EnrollmentId == id, ct));
    }, ct);

    private async Task AdminAsync(Guid id, CancellationToken ct)
    {
        // Coordinate with PostgreSQL account changes too. SQLite already owns its writer lock.
        var rows = db.Database.IsNpgsql()
            ? await db.ApplicationUsers.FromSqlInterpolated($"SELECT * FROM \"ApplicationUsers\" WHERE \"Id\" = {id} FOR SHARE").AsNoTracking().ToListAsync(ct)
            : await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == id).ToListAsync(ct);
        if (rows.SingleOrDefault() is not { IsActive: true, MustChangePassword: false, Role: "Admin" }) throw new ProvisioningException("Forbidden", 403);
    }
    private async Task<DeviceEnrollment> RequestAsync(Guid id, CancellationToken ct)
    {
        var e = await db.DeviceEnrollments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ProvisioningException("NotFound", 404);
        if (e.Environment != options.EnvironmentId) throw new ProvisioningException("EnvironmentMismatch");
        return e;
    }
    private async Task<RegisteredDevice> DeviceAsync(Guid id, CancellationToken ct)
    {
        var d = await db.RegisteredDevices.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ProvisioningException("NotFound", 404);
        if (d.Status != DeviceProvisioningStatus.PendingCredential) throw new ProvisioningException("TerminalDevice");
        return d;
    }
    private async Task<RegisteredDevice> BindingAsync(DeviceEnrollment e, CancellationToken ct)
    {
        var d = await DeviceAsync(e.DeviceId, ct);
        if (d.LineId != e.LineId || d.BindingVersion != e.SourceBindingVersion) throw new ProvisioningException("BindingChanged");
        await ActiveLineAsync(e.LineId, ct); return d;
    }
    private async Task ActiveLineAsync(Guid id, CancellationToken ct)
    { if (!await db.ProductionLines.AnyAsync(x => x.Id == id && x.IsActive, ct)) throw new ProvisioningException("InactiveLine"); }
    private async Task ChallengeBudget(Guid id, CancellationToken ct)
    {
        if (await db.EnrollmentChallenges.CountAsync(x => x.EnrollmentId == id && x.CreatedAtUtc > Now.AddMinutes(-1), ct) >= 5)
            throw new ProvisioningException("RateLimited", 429);
    }
    private EnrollmentChallengeOutput NewChallenge(DeviceEnrollment e, ChallengePurpose purpose, DateTime deadline)
    {
        var nonce = DeviceEnrollmentProof.Encode(RandomNumberGenerator.GetBytes(32));
        var c = new EnrollmentChallenge { Id = Guid.NewGuid(), EnrollmentId = e.Id, Purpose = purpose, CreatedAtUtc = Now,
            NonceHash = DeviceEnrollmentProof.Hash(nonce), ExpiresAtUtc = new[] { Now.AddMinutes(2), deadline }.Min() };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, purpose = purpose.ToString(), audience = e.Environment,
            enrollmentId = e.Id, deviceId = e.DeviceId, lineId = e.LineId, bindingVersion = e.SourceBindingVersion,
            targetBindingVersion = e.TargetBindingVersion, csrSha256 = e.CsrSha256, spkiSha256 = e.SpkiSha256,
            challengeId = c.Id, nonce, expiresAt = new DateTimeOffset(Utc(c.ExpiresAtUtc)).ToUnixTimeSeconds() });
        c.PayloadHash = DeviceEnrollmentProof.Hash(payload); db.EnrollmentChallenges.Add(c);
        return new(c.Id, DeviceEnrollmentProof.ProtectedHeader, DeviceEnrollmentProof.Encode(payload), Utc(c.ExpiresAtUtc));
    }
    private async Task ConsumeAsync(DeviceEnrollment e, EnrollmentProofInput input, ChallengePurpose purpose, CancellationToken ct)
    {
        var c = await db.EnrollmentChallenges.SingleOrDefaultAsync(x => x.Id == input.ChallengeId && x.EnrollmentId == e.Id, ct) ?? throw new ProvisioningException("InvalidProof", 400);
        if (c.Purpose != purpose) throw new ProvisioningException("InvalidProof", 400);
        if (c.ConsumedAtUtc is not null) throw new ProvisioningException("ChallengeConsumed");
        if (c.ExpiresAtUtc <= Now) throw new ProvisioningException("ChallengeExpired");
        var payload = DeviceEnrollmentProof.Verify(input.Jws, e.SpkiBase64!, c.PayloadHash);
        using var json = JsonDocument.Parse(payload);
        if (!DeviceEnrollmentProof.MatchesHash(json.RootElement.GetProperty("nonce").GetString()!, c.NonceHash)) throw new ProvisioningException("InvalidProof", 400);
        c.ConsumedAtUtc = Now; c.Revision++;
    }
}
