using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

// All methods run INSIDE the caller's existing ProvisioningStore transaction.
public sealed class DeviceEnrollmentLifecycle(IpqcDbContext db, ProvisioningStore store, TimeProvider clock)
{
    public static ProvisioningSnapshot Snapshot(DeviceEnrollment e, RegisteredDevice d, DeviceCredential? k = null) =>
        new(d.DisplayName, d.Revision, Status: d.Status.ToString(), LineId: e.LineId, BindingVersion: e.SourceBindingVersion,
            EnrollmentId: e.Id, EnrollmentState: e.State.ToString(), EnrollmentRevision: e.Revision,
            CredentialId: k?.Id, CredentialState: k?.State.ToString(), SpkiSha256: e.SpkiSha256, CertificateSha256: k?.CertificateSha256);
    public void Audit(DeviceEnrollment e, RegisteredDevice d, string action, ProvisioningActor actor,
        ProvisioningSnapshot? before, DeviceCredential? k = null, string? reason = null, Guid? change = null) =>
        store.Audit(null, d.Id, action, actor, before, Snapshot(e, d, k), reason, change, e.Id, k?.Id);

    public async Task InvalidateDeviceAsync(RegisteredDevice device, string reason, ProvisioningActor actor, Guid change, CancellationToken ct)
    {
        var enrollments = await db.DeviceEnrollments.Where(x => x.DeviceId == device.Id && (int)x.State <= 6).ToListAsync(ct);
        foreach (var e in enrollments) await CloseAsync(e, device, EnrollmentState.Cancelled, reason, actor, change, ct);
    }
    public async Task CloseAsync(DeviceEnrollment e, RegisteredDevice d, EnrollmentState terminal, string reason,
        ProvisioningActor actor, Guid change, CancellationToken ct)
    {
        var k = await db.DeviceCredentials.SingleOrDefaultAsync(x => x.EnrollmentId == e.Id, ct);
        var before = Snapshot(e, d, k);
        e.State = terminal; e.TerminalReason = reason; e.Revision++;
        foreach (var c in await db.EnrollmentChallenges.Where(x => x.EnrollmentId == e.Id && x.ConsumedAtUtc == null).ToListAsync(ct))
        { c.ConsumedAtUtc = clock.GetUtcNow().UtcDateTime; c.Revision++; }
        if (k is not null)
        {
            k.ExternalRevocationPending = k.CertificatePem is not null || k.State == CandidateCredentialState.OutcomeUnknown;
            k.State = CandidateCredentialState.Revoked; k.Revision++;
        }
        Audit(e, d, terminal == EnrollmentState.Expired ? "EnrollmentExpired" : "EnrollmentCancelled", actor, before, k, reason, change);
    }
}
