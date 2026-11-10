using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

// Historical operation drain only. New signing is never part of recovery.
public sealed class DeviceCredentialIssuance(IpqcDbContext db, ProvisioningStore store, DeviceEnrollmentLifecycle lifecycle,
    IDeviceCertificateIssuer issuer, DeviceEnrollmentOptions options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Claim(Guid Id, Guid Lease, Guid EnrollmentId, Guid DeviceId, Guid LineId,
        long BindingVersion, Guid OperationId, string IssuerId, string RootPem, string Environment);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        if (!options.CleanupEnabled || string.IsNullOrWhiteSpace(options.EnvironmentId)) return false;
        db.ChangeTracker.Clear();
        var (worked, claim) = await store.WriteAsync(async () =>
        {
            var now = Now;
            var next = await (from request in db.DeviceEnrollments
                join credential in db.DeviceCredentials on request.Id equals credential.EnrollmentId into credentials
                from candidate in credentials.DefaultIfEmpty()
                where request.Environment == options.EnvironmentId &&
                    ((int)request.State <= 6 || (candidate != null && (candidate.ExternalRevocationPending ||
                        candidate.State == CandidateCredentialState.OutcomeUnknown || candidate.State == CandidateCredentialState.IssuedUnconfirmed))) &&
                    (candidate == null || candidate.LeaseUntilUtc == null || candidate.LeaseUntilUtc <= now)
                orderby candidate == null ? null : candidate.LeaseUntilUtc, request.Id
                select new { Enrollment = request, Credential = candidate }).FirstOrDefaultAsync(ct);
            if (next is null) return (false, (Claim?)null);
            var e = next.Enrollment; var k = next.Credential;
            var d = await db.RegisteredDevices.SingleAsync(x => x.Id == e.DeviceId, ct);
            if ((int)e.State <= 6)
                await lifecycle.CloseAsync(e, d, e.ExpiresAtUtc <= now ? EnrollmentState.Expired : EnrollmentState.Cancelled,
                    "DeviceProvisioningRetired", ProvisioningActor.System, Guid.NewGuid(), ct);
            if (k is null) return (true, (Claim?)null);
            var before = DeviceEnrollmentLifecycle.Snapshot(e, d, k);
            // Also reconcile historical terminal rows with an unknown/unconfirmed candidate.
            k.ExternalRevocationPending |= k.CertificatePem is not null || k.State is
                CandidateCredentialState.OutcomeUnknown or CandidateCredentialState.IssuedUnconfirmed;
            if (!k.ExternalRevocationPending) return (true, (Claim?)null);
            k.State = CandidateCredentialState.Revoked;
            k.LeaseId = Guid.NewGuid(); k.LeaseUntilUtc = now.AddSeconds(30); k.Revision++;
            lifecycle.Audit(e, d, "CredentialRevocationStarted", ProvisioningActor.System, before, k);
            return (true, new Claim(k.Id, k.LeaseId.Value, e.Id, k.DeviceId, k.LineId, k.BindingVersion,
                k.IssuanceOperationId, k.IssuerId, k.RootCertificatePem, e.Environment));
        }, ct);
        if (claim is null) return worked;

        // No DB transaction/lock crosses the adapter boundary. Recover never falls through to Issue.
        string? failure = null;
        try
        {
            if (!issuer.Available) failure = "IssuerUnavailable";
            else if (issuer.Profile.Id != claim.IssuerId || issuer.Profile.RootCertificatePem != claim.RootPem)
                failure = "IssuerMismatch";
            else
            {
                var original = await issuer.RecoverAsync(claim.OperationId, ct);
                if (original.Outcome == IssuerOutcome.Unknown) failure = "IssuerOutcomeUnknown";
                else
                    // Even definitive NotIssued needs an acknowledged cancellation fence against late publication.
                    // The original public result/journal is retained; existing credential metadata is not rewritten.
                    await issuer.RevokeAsync(claim.OperationId, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { failure = "IssuerOutcomeUnknown"; }

        db.ChangeTracker.Clear();
        await store.WriteAsync(async () =>
        {
            var k = await db.DeviceCredentials.SingleAsync(x => x.Id == claim.Id, ct);
            var e = await db.DeviceEnrollments.SingleAsync(x => x.Id == claim.EnrollmentId, ct);
            if (k.LeaseId != claim.Lease || k.EnrollmentId != claim.EnrollmentId || k.DeviceId != claim.DeviceId ||
                k.LineId != claim.LineId || k.BindingVersion != claim.BindingVersion || k.IssuanceOperationId != claim.OperationId ||
                k.IssuerId != claim.IssuerId || k.RootCertificatePem != claim.RootPem || e.Environment != claim.Environment)
                return false;
            var d = await db.RegisteredDevices.SingleAsync(x => x.Id == k.DeviceId, ct);
            var before = DeviceEnrollmentLifecycle.Snapshot(e, d, k);
            k.Revision++; k.LastErrorCode = failure;
            k.LeaseId = null; k.LeaseUntilUtc = Now.AddSeconds(5);
            if (failure is null) k.ExternalRevocationPending = false;
            lifecycle.Audit(e, d, failure is null ? "CredentialRevocationRecorded" : "CredentialIssuanceDeferred",
                ProvisioningActor.System, before, k);
            return true;
        }, ct);
        return true;
    }
}

public sealed class DeviceCredentialIssuanceWorker(IServiceScopeFactory scopes, DeviceEnrollmentOptions options,
    ILogger<DeviceCredentialIssuanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CleanupEnabled || !options.WorkerEnabled) return;
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
            catch (Exception) { logger.LogWarning("Retired credential cleanup deferred; the original operation remains pending."); }
        }
    }
}
