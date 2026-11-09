using System.Security.Cryptography;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class DeviceCredentialIssuance(IpqcDbContext db, ProvisioningStore store, DeviceEnrollmentLifecycle lifecycle,
    IDeviceCertificateIssuer issuer, DeviceEnrollmentOptions options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Claim(Guid Id, Guid Lease, bool Cleanup, DeviceIssuanceRequest Request);
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        if (!issuer.Available) return false;
        db.ChangeTracker.Clear();
        var claim = await store.WriteAsync(async () =>
        {
            var k = await db.DeviceCredentials.Where(x =>
                (x.State == CandidateCredentialState.PendingIssuance || x.State == CandidateCredentialState.OutcomeUnknown || x.ExternalRevocationPending) &&
                (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= Now))
                .Join(db.DeviceEnrollments.Where(x => x.Environment == options.EnvironmentId), x => x.EnrollmentId, x => x.Id, (x, _) => x)
                .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (k is null) return null;
            var e = await db.DeviceEnrollments.SingleAsync(x => x.Id == k.EnrollmentId, ct);
            var d = await db.RegisteredDevices.SingleAsync(x => x.Id == k.DeviceId, ct);
            if ((int)e.State <= 6 && (e.ExpiresAtUtc <= Now || !await BindingValid(e, d, ct)))
                await lifecycle.CloseAsync(e, d, e.ExpiresAtUtc <= Now ? EnrollmentState.Expired : EnrollmentState.Cancelled,
                    "BindingOrDeadlineChanged", ProvisioningActor.System, Guid.NewGuid(), ct);
            var cleanup = (int)e.State > 6 || k.State is CandidateCredentialState.Revoked or CandidateCredentialState.Failed;
            if (cleanup && !k.ExternalRevocationPending) return null;
            var before = DeviceEnrollmentLifecycle.Snapshot(e, d, k);
            k.LeaseId = Guid.NewGuid(); k.LeaseUntilUtc = Now.AddSeconds(30); k.Revision++;
            if (!cleanup) { k.State = CandidateCredentialState.OutcomeUnknown; e.State = EnrollmentState.Issuing; e.Revision++; }
            lifecycle.Audit(e, d, cleanup ? "CredentialRevocationStarted" : "CredentialIssuanceStarted", ProvisioningActor.System, before, k);
            return new Claim(k.Id, k.LeaseId.Value, cleanup, new(k.IssuanceOperationId, k.DeviceId, k.Id, k.SpkiBase64,
                k.RootCertificatePem, Utc(k.NotBeforeUtc), Utc(k.NotAfterUtc)));
        }, ct);
        if (claim is null) return false;

        // No database transaction or provisioning lock is held across ANY signer operation.
        DeviceIssuanceResult result = new(IssuerOutcome.Unknown); string? failure = null;
        try
        {
            if (claim.Cleanup) await issuer.RevokeAsync(claim.Request.OperationId, ct);
            else
            {
                result = await issuer.RecoverAsync(claim.Request.OperationId, ct);
                if (result.Outcome == IssuerOutcome.NotIssued) result = await issuer.IssueAsync(claim.Request, ct);
                if (result.Outcome == IssuerOutcome.Issued) DeviceCertificateProfile.Validate(claim.Request, result.CertificatePem!, Now);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CryptographicException) { failure = "InvalidIssuerResult"; }
        catch (Exception) { failure = "IssuerOutcomeUnknown"; }

        db.ChangeTracker.Clear();
        await store.WriteAsync(async () =>
        {
            var k = await db.DeviceCredentials.SingleAsync(x => x.Id == claim.Id, ct);
            if (k.LeaseId != claim.Lease) return false; // Another recovery owns the lease; it must recover the original result.
            var e = await db.DeviceEnrollments.SingleAsync(x => x.Id == k.EnrollmentId, ct);
            var d = await db.RegisteredDevices.SingleAsync(x => x.Id == k.DeviceId, ct);
            var before = DeviceEnrollmentLifecycle.Snapshot(e, d, k);
            if ((int)e.State <= 6 && (e.ExpiresAtUtc <= Now || !await BindingValid(e, d, ct)))
                await lifecycle.CloseAsync(e, d, e.ExpiresAtUtc <= Now ? EnrollmentState.Expired : EnrollmentState.Cancelled,
                    "BindingOrDeadlineChanged", ProvisioningActor.System, Guid.NewGuid(), ct);
            var cancelled = (int)e.State > 6 || k.State == CandidateCredentialState.Revoked;
            k.Revision++; k.LastErrorCode = failure;
            k.LeaseId = null; k.LeaseUntilUtc = Now.AddSeconds(5);
            if (claim.Cleanup)
            {
                if (failure is null) k.ExternalRevocationPending = false;
                lifecycle.Audit(e, d, failure is null ? "CredentialRevocationRecorded" : "CredentialIssuanceDeferred", ProvisioningActor.System, before, k);
            }
            else if (failure == "InvalidIssuerResult")
            {
                k.State = cancelled ? CandidateCredentialState.Revoked : CandidateCredentialState.Failed;
                k.ExternalRevocationPending = true;
                lifecycle.Audit(e, d, "CredentialIssuanceFailed", ProvisioningActor.System, before, k);
            }
            else if (failure is null && result.Outcome == IssuerOutcome.Issued)
            {
                k.CertificateSha256 = DeviceCertificateProfile.Validate(claim.Request, result.CertificatePem!, Now);
                k.CertificatePem = result.CertificatePem; k.IssuedAtUtc = Now;
                k.State = cancelled ? CandidateCredentialState.Revoked : CandidateCredentialState.IssuedUnconfirmed;
                k.ExternalRevocationPending = cancelled;
                if (!cancelled) { e.State = EnrollmentState.IssuedUnconfirmed; e.Revision++; }
                lifecycle.Audit(e, d, cancelled ? "CredentialLateResultRevoked" : "CredentialCandidateIssued", ProvisioningActor.System, before, k);
            }
            else
            {
                k.State = cancelled ? CandidateCredentialState.Revoked : CandidateCredentialState.OutcomeUnknown;
                if (cancelled) k.ExternalRevocationPending = true;
                k.LastErrorCode ??= "IssuerOutcomeUnknown";
                lifecycle.Audit(e, d, "CredentialIssuanceDeferred", ProvisioningActor.System, before, k);
            }
            return true;
        }, ct);
        return true;
    }
    private async Task<bool> BindingValid(DeviceEnrollment e, RegisteredDevice d, CancellationToken ct) =>
        d.Status == DeviceProvisioningStatus.PendingCredential && d.LineId == e.LineId && d.BindingVersion == e.SourceBindingVersion &&
        await db.ProductionLines.AnyAsync(x => x.Id == e.LineId && x.IsActive, ct);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

public sealed class DeviceCredentialIssuanceWorker(IServiceScopeFactory scopes, DeviceEnrollmentOptions options,
    ILogger<DeviceCredentialIssuanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.WorkerEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                for (var i = 0; i < 8; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<DeviceCredentialIssuance>().ProcessNextAsync(stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Enrollment issuance/recovery deferred; durable operation remains pending."); }
        }
    }
}
