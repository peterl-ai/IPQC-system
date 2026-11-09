using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class EnrollmentRevocationTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("rebind")]
    [InlineData("unbind")]
    [InlineData("revoke")]
    [InlineData("replace")]
    [InlineData("expire-worker")]
    [InlineData("expire-invitation")]
    public async Task Failed_candidate_retains_cleanup_across_closure_until_original_signer_operation_is_acknowledged(string action)
    {
        await using var f = await EnrollmentFixture.CreateAsync();
        var device = await f.BoundDevice();
        var otherLine = await f.App.LineAsync("OTHER");
        var replacement = await f.App.DeviceAsync("Synthetic replacement");
        var original = await FailedCandidate(f, device);
        var immutableCandidate = Identity(original);
        var originalEnrollment = await f.Db(db => db.DeviceEnrollments.AsNoTracking().SingleAsync());
        var immutableEnrollment = Identity(originalEnrollment);
        var history = await f.Db(async db => (await db.ProvisioningAuditEvents.AsNoTracking().ToListAsync())
            .ToDictionary(x => x.Id, x => JsonSerializer.Serialize(x)));
        var tombstone = Path.Combine(f.Options.ReceiptDirectory, original.IssuanceOperationId.ToString("N") + ".revoked");

        f.Issuer.FailRevocation = true;
        if (action.StartsWith("expire-"))
        {
            f.Clock.Advance(TimeSpan.FromMinutes(31));
            if (action == "expire-worker") Assert.True(await f.Process());
            else
            {
                // The synthetic clock also expires the original personal JWT.
                using var admin = await TestAccounts.ClientAsync(f.App.Factory, "admin");
                (await admin.PostAsJsonAsync($"/api/admin/devices/{device.Id}/enrollments",
                    new EnrollmentCreateInput(device.Revision))).EnsureSuccessStatusCode();
            }
        }
        else if (action == "cancel")
            (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{original.EnrollmentId}/cancel",
                new EnrollmentCancelInput(originalEnrollment.Revision, "Cancel rejected issuer result"))).EnsureSuccessStatusCode();
        else if (action == "rebind")
            await f.App.DeviceAction(device, "rebind", new DeviceBindingInput(otherLine.Id, device.Revision, "Move reservation"));
        else if (action == "replace")
            await f.App.DeviceAction(device, "replace", new DeviceReplaceInput(replacement.Id, device.Revision, replacement.Revision, "Replace PAD"));
        else
            await f.App.DeviceAction(device, action, new DeviceReasonInput(device.Revision, "Retire reservation"));

        var closed = await Candidate(f);
        Assert.Equal(CandidateCredentialState.Revoked, closed.State);
        Assert.True(closed.ExternalRevocationPending);
        Assert.Equal(immutableCandidate, Identity(closed));
        var closedEnrollment = await f.Db(db => db.DeviceEnrollments.AsNoTracking().SingleAsync(x => x.Id == original.EnrollmentId));
        Assert.Equal(action.StartsWith("expire-") ? EnrollmentState.Expired : EnrollmentState.Cancelled, closedEnrollment.State);
        Assert.Equal(immutableEnrollment, Identity(closedEnrollment));

        if (action != "expire-worker")
        {
            f.Clock.Advance(TimeSpan.FromSeconds(6));
            Assert.True(await f.Process());
        }
        Assert.Equal(new[] { original.IssuanceOperationId }, f.Issuer.Revocations);
        Assert.Empty(f.Issuer.RevocationAcknowledgements);
        Assert.True((await Candidate(f)).ExternalRevocationPending);
        Assert.Equal("IssuerOutcomeUnknown", (await Candidate(f)).LastErrorCode);
        Assert.False(File.Exists(tombstone));
        Assert.Equal(0, await AcknowledgementCount(f));

        // Observe the pending flag while the real signer call is still awaiting acknowledgement.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Issuer.FailRevocation = false;
        f.Issuer.BeforeRevoke = async () => { entered.SetResult(); await release.Task; };
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        var cleanup = f.Process();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True((await Candidate(f)).ExternalRevocationPending);
            Assert.Empty(f.Issuer.RevocationAcknowledgements);
            Assert.Equal(0, await AcknowledgementCount(f));
        }
        finally { release.TrySetResult(); }
        Assert.True(await cleanup);

        Assert.Equal(new[] { original.IssuanceOperationId, original.IssuanceOperationId }, f.Issuer.Revocations);
        Assert.Equal(new[] { original.IssuanceOperationId }, f.Issuer.RevocationAcknowledgements);
        Assert.True(File.Exists(tombstone));
        var acknowledged = await Candidate(f);
        Assert.False(acknowledged.ExternalRevocationPending);
        Assert.Equal(CandidateCredentialState.Revoked, acknowledged.State);
        Assert.Equal(immutableCandidate, Identity(acknowledged));
        Assert.Null(acknowledged.LastErrorCode);
        Assert.Equal(1, f.Issuer.Issues);

        f.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(await f.Process());
        Assert.Equal(2, f.Issuer.Revocations.Count);
        Assert.Equal(1, await AcknowledgementCount(f));
        var events = await f.Db(db => db.ProvisioningAuditEvents.AsNoTracking().ToListAsync());
        var recorded = Assert.Single(events, x => x.Action == "CredentialRevocationRecorded");
        Assert.Equal(ProvisioningActorKind.System, recorded.ActorKind);
        Assert.Equal(original.Id, recorded.CredentialId);
        Assert.Equal(original.EnrollmentId, recorded.EnrollmentId);
        foreach (var entry in history) Assert.Equal(entry.Value, JsonSerializer.Serialize(events.Single(x => x.Id == entry.Key)));
        if (action is "rebind" or "unbind" or "revoke" or "replace")
        {
            var terminal = Assert.Single(events, x => x.Action == "EnrollmentCancelled");
            Assert.Contains(events, x => x.ChangeId == terminal.ChangeId && x.Action == (action switch
            { "rebind" => "ReservationRebound", "unbind" => "ReservationUnbound", "revoke" => "DeviceRevoked", _ => "DeviceReplaced" }));
        }
        await f.Db(async db =>
        {
            foreach (var row in await db.RegisteredDevices.AsNoTracking().ToListAsync())
            {
                var output = DeviceRegistryService.Output(row);
                Assert.Equal("NotEnrolled", output.CredentialStatus); Assert.False(output.CanExecuteLineShared);
            }
            Assert.False(await db.DeviceCredentials.AnyAsync(x => x.DeviceId == replacement.Id));
            return 0;
        });
    }

    [Fact]
    public async Task Audit_failure_after_signer_ack_rolls_back_flag_and_retries_same_idempotent_operation()
    {
        var failure = new RevocationAuditFailure();
        await using var f = await EnrollmentFixture.CreateAsync(failure);
        var device = await f.BoundDevice(); var original = await FailedCandidate(f, device);
        var enrollment = await f.Get(device.Id);
        (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{enrollment.Id}/cancel",
            new EnrollmentCancelInput(enrollment.Revision, "Cancel candidate"))).EnsureSuccessStatusCode();
        failure.Armed = true; f.Clock.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Process());
        Assert.True(failure.Fired);
        Assert.Equal(new[] { original.IssuanceOperationId }, f.Issuer.RevocationAcknowledgements);
        Assert.True((await Candidate(f)).ExternalRevocationPending);
        Assert.Equal(0, await AcknowledgementCount(f));
        f.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await f.Process());
        Assert.Equal(new[] { original.IssuanceOperationId, original.IssuanceOperationId }, f.Issuer.Revocations);
        Assert.False((await Candidate(f)).ExternalRevocationPending);
        Assert.Equal(Identity(original), Identity(await Candidate(f)));
        Assert.Equal(1, await AcknowledgementCount(f));
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(await f.Process()); Assert.Equal(2, f.Issuer.Revocations.Count);
    }

    private static async Task<DeviceCredential> FailedCandidate(EnrollmentFixture f, DeviceOutput device)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await f.Approve(device, await f.Prove(await f.Invite(device), key));
        f.Issuer.Transform = (_, _) => new(IssuerOutcome.Issued, f.Issuer.Profile.RootCertificatePem);
        Assert.True(await f.Process());
        var candidate = await Candidate(f);
        Assert.Equal(CandidateCredentialState.Failed, candidate.State);
        Assert.Null(candidate.CertificatePem); Assert.True(candidate.ExternalRevocationPending);
        Assert.Equal("InvalidIssuerResult", candidate.LastErrorCode);
        // A real isolated test-issuer result exists, even though it was rejected/not stored in the DB.
        Assert.Equal(IssuerOutcome.Issued, (await f.DevelopmentIssuer.RecoverAsync(candidate.IssuanceOperationId, default)).Outcome);
        return candidate;
    }

    private static Task<DeviceCredential> Candidate(EnrollmentFixture f) => f.Db(db => db.DeviceCredentials.AsNoTracking().SingleAsync());
    private static Task<int> AcknowledgementCount(EnrollmentFixture f) => f.Db(db => db.ProvisioningAuditEvents.CountAsync(x => x.Action == "CredentialRevocationRecorded"));
    private static string Identity(DeviceCredential x) => JsonSerializer.Serialize(new
    { x.Id, x.EnrollmentId, x.DeviceId, x.LineId, x.BindingVersion, x.IssuanceOperationId, x.IssuerId, x.RootCertificatePem, x.SpkiBase64, x.NotBeforeUtc, x.NotAfterUtc, x.CertificatePem, x.CertificateSha256, x.IssuedAtUtc });
    private static string Identity(DeviceEnrollment x) => JsonSerializer.Serialize(new
    { x.Id, x.DeviceId, x.LineId, x.SourceBindingVersion, x.TargetBindingVersion, x.Environment, x.CreatedAtUtc, x.ExpiresAtUtc, x.CreatedByUserId, x.CsrDerBase64, x.CsrSha256, x.SpkiBase64, x.SpkiSha256, x.ProofVerifiedAtUtc, x.ApprovedByUserId, x.ApprovedAtUtc, x.ApprovalOperationId, x.ApprovalContentHash });

    private sealed class RevocationAuditFailure : SaveChangesInterceptor
    {
        public bool Armed; public bool Fired;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<ProvisioningAuditEvent>().Any(x => x.State == EntityState.Added && x.Entity.Action == "CredentialRevocationRecorded"))
            { Armed = false; Fired = true; throw new InvalidOperationException("Injected revocation audit failure."); }
            return ValueTask.FromResult(result);
        }
    }
}
