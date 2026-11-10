using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class DeviceEnrollmentService(IpqcDbContext db, ProvisioningStore store, DeviceEnrollmentLifecycle lifecycle,
    DeviceEnrollmentOptions options, TimeProvider clock)
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
    public Task<EnrollmentInvitation> CreateAsync(Guid device, EnrollmentCreateInput input, Guid actor, string name, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();

    public Task<EnrollmentChallengeOutput> ChallengeAsync(Guid id, EnrollmentChallengeInput input, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();

    public Task<EnrollmentOutput> ProofAsync(Guid id, EnrollmentProofInput input, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();

    public Task<EnrollmentChallengeOutput> ReceiptChallengeAsync(Guid id, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();

    public Task<EnrollmentOutput> ReceiptAsync(Guid id, EnrollmentProofInput input, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();

    public Task<EnrollmentOutput> ApproveAsync(Guid id, EnrollmentApproveInput input, Guid actor, string name, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();

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
}
