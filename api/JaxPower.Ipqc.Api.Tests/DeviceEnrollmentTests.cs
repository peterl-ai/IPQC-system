using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DeviceEnrollmentTests
{
    [Fact]
    public async Task Complete_candidate_flow_preserves_non_authority_hash_storage_truthful_audit_and_recoverable_receipts()
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invitation = await f.Invite(d); Assert.False(invitation.Enrollment.CanExecuteLineShared);
        var stored = await f.Db(db => db.DeviceEnrollments.SingleAsync());
        Assert.NotEqual(invitation.InvitationSecret, stored.InvitationHash); Assert.Equal(64, stored.InvitationHash.Length);
        var challenge = await f.Challenge(invitation, key);
        using var payload = JsonDocument.Parse(DeviceEnrollmentProof.Decode(challenge.Payload));
        Assert.Equal(d.Id, payload.RootElement.GetProperty("deviceId").GetGuid()); Assert.Equal(d.LineId, payload.RootElement.GetProperty("lineId").GetGuid());
        Assert.Equal(d.BindingVersion, payload.RootElement.GetProperty("bindingVersion").GetInt64()); Assert.Equal(f.Options.EnvironmentId, payload.RootElement.GetProperty("audience").GetString());
        var before = await f.State();
        Assert.Equal(HttpStatusCode.Conflict, (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{stored.Id}/approve", f.Approval(d, invitation.Enrollment))).StatusCode);
        Assert.Equal(before, await f.State());
        var proof = EnrollmentFixture.Sign(challenge, key);
        var response = await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{stored.Id}/proof", proof); response.EnsureSuccessStatusCode();
        var verified = (await response.Content.ReadFromJsonAsync<EnrollmentOutput>())!;
        Assert.Equal("PendingApproval", verified.State);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{stored.Id}/proof", proof)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{stored.Id}/challenge", new EnrollmentChallengeInput(invitation.InvitationSecret, EnrollmentFixture.Csr(key, d.Id)))).StatusCode);
        var approval = f.Approval(d, verified);
        (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{stored.Id}/approve", approval)).EnsureSuccessStatusCode();
        var audits = await f.App.AuditCount();
        (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{stored.Id}/approve", approval)).EnsureSuccessStatusCode();
        Assert.Equal(audits, await f.App.AuditCount());
        Assert.True(await f.Process()); var issued = await f.Get(d.Id);
        Assert.True(issued.State == "IssuedUnconfirmed", $"State={issued.State}; credential={issued.Candidate?.State}; error={issued.Candidate?.LastErrorCode}; signer={f.Issuer.Failure}"); Assert.Equal("IssuedUnconfirmed", issued.Candidate!.State);
        Assert.NotNull(issued.Candidate.CertificatePem); Assert.False(issued.Candidate.CanExecuteLineShared);
        Assert.Equal(d.BindingVersion + 1, issued.Candidate.BindingVersion);
        var unchanged = await f.App.GetDevice(d.Id);
        Assert.Equal(d, unchanged); Assert.Equal("NotEnrolled", unchanged.CredentialStatus); Assert.False(unchanged.CanExecuteLineShared);
        var receiptChallenge = await (await f.Bootstrap.PostAsync($"/bootstrap/v1/enrollments/{stored.Id}/receipt-challenge", null)).Content.ReadFromJsonAsync<EnrollmentChallengeOutput>();
        var receiptProof = EnrollmentFixture.Sign(receiptChallenge!, key);
        var receipt = await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{stored.Id}/receipt", receiptProof); receipt.EnsureSuccessStatusCode();
        Assert.Equal(issued, await receipt.Content.ReadFromJsonAsync<EnrollmentOutput>());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{stored.Id}/receipt", receiptProof)).StatusCode);
        var events = await f.App.DeviceAudit(d.Id);
        Assert.Contains(events.Items, x => x.ActorKind == "ApplicantKey" && x.ActorUserId is null && x.ActorDeviceId == d.Id);
        Assert.Contains(events.Items, x => x.ActorKind == "System" && x.ActorUserId is null);
        Assert.Contains(events.Items, x => x.Action == "EnrollmentApproved" && x.ActorKind == "Admin" && x.ActorUserId == TestAccounts.AdminId);
        Assert.DoesNotContain(events.Items, x => x.ActorKind == "Device");
        var json = JsonSerializer.Serialize(events); Assert.DoesNotContain(invitation.InvitationSecret, json);
        Assert.DoesNotContain(payload.RootElement.GetProperty("nonce").GetString()!, json);
        Assert.DoesNotContain("PRIVATE KEY", json);
    }

    [Theory]
    [InlineData("der")]
    [InlineData("wrong-key")]
    [InlineData("algorithm")]
    [InlineData("payload")]
    [InlineData("short")]
    [InlineData("padding")]
    [InlineData("purpose")]
    public async Task Invalid_JWS_and_key_inputs_do_not_consume_challenges_or_write_audit(string attack)
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invite = await f.Invite(d); var challenge = await f.Challenge(invite, key);
        var proof = EnrollmentFixture.Sign(challenge, attack == "wrong-key" ? wrong : key,
            attack == "der" ? DSASignatureFormat.Rfc3279DerSequence : DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var parts = proof.Jws.Split('.');
        if (attack == "algorithm") parts[0] = DeviceEnrollmentProof.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        if (attack == "payload") parts[1] = DeviceEnrollmentProof.Encode(Encoding.UTF8.GetBytes("{\"audience\":\"other\"}"));
        if (attack == "short") parts[2] = DeviceEnrollmentProof.Encode(new byte[63]);
        if (attack == "padding") parts[2] += "=";
        proof = proof with { Jws = string.Join('.', parts) };
        var before = await f.State();
        var endpoint = attack == "purpose" ? "receipt" : "proof";
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/{endpoint}", proof)).StatusCode);
        Assert.Equal(before, await f.State());
        (await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/proof", EnrollmentFixture.Sign(challenge, key))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Expiry_wrong_invitation_CSR_subject_and_curve_and_key_replacement_fail_closed()
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); var invite = await f.Invite(d);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var unsupported = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var path = $"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/challenge"; var before = await f.State();
        foreach (var bad in new[] { new EnrollmentChallengeInput("wrong", EnrollmentFixture.Csr(key,d.Id)),
            new EnrollmentChallengeInput(invite.InvitationSecret, EnrollmentFixture.Csr(key,Guid.NewGuid())),
            new EnrollmentChallengeInput(invite.InvitationSecret, EnrollmentFixture.Csr(unsupported,d.Id)),
            new EnrollmentChallengeInput(invite.InvitationSecret, "not-a-csr") })
            Assert.False((await f.Bootstrap.PostAsJsonAsync(path, bad)).IsSuccessStatusCode);
        Assert.Equal(before, await f.State());
        var challenge = await f.Challenge(invite, key);
        f.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/proof", EnrollmentFixture.Sign(challenge,key))).StatusCode);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Bootstrap.PostAsJsonAsync(path, new EnrollmentChallengeInput(invite.InvitationSecret, EnrollmentFixture.Csr(other,d.Id)))).StatusCode);
        f.Clock.Advance(TimeSpan.FromMinutes(8));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Bootstrap.PostAsJsonAsync(path, new EnrollmentChallengeInput(invite.InvitationSecret, EnrollmentFixture.Csr(key,d.Id)))).StatusCode);
    }

    [Theory]
    [InlineData("pqe", 403)] [InlineData("dev-ipqa-1", 403)] [InlineData("anonymous", 401)] [InlineData("forced", 403)] [InlineData("revoked", 401)]
    public async Task Enrollment_admin_routes_preserve_RBAC(string user, int status)
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); var invite = await f.Invite(d);
        if (user is "forced" or "revoked") await f.Db(async db => { var row = await db.ApplicationUsers.SingleAsync(x=>x.Id==TestAccounts.AdminId); row.MustChangePassword = user == "forced"; row.IsActive = user != "revoked"; await db.SaveChangesAsync(); return 0; });
        using var client = user == "anonymous" ? f.App.Factory.CreateClient() : user is "forced" or "revoked" ? f.App.Admin : await TestAccounts.ClientAsync(f.App.Factory,user);
        var before = await f.State();
        Assert.Equal(status, (int)(await client.GetAsync($"/api/admin/devices/{d.Id}/enrollments")).StatusCode);
        Assert.Equal(status, (int)(await client.PostAsJsonAsync($"/api/admin/devices/{d.Id}/enrollments", new EnrollmentCreateInput(d.Revision))).StatusCode);
        Assert.Equal(status, (int)(await client.PostAsJsonAsync($"/api/admin/enrollments/{invite.Enrollment.Id}/approve", f.Approval(d,invite.Enrollment))).StatusCode);
        Assert.Equal(status, (int)(await client.PostAsJsonAsync($"/api/admin/enrollments/{invite.Enrollment.Id}/cancel", new EnrollmentCancelInput(1,"Cancel"))).StatusCode);
        Assert.Equal(before, await f.State());
    }

    [Fact]
    public async Task Bootstrap_default_production_guards_limits_and_unconfigured_PKI_remain_fail_closed()
    {
        var host = new EnrollmentTestHost { EnvironmentName = "Production" };
        Assert.Throws<InvalidOperationException>(() => new DeviceEnrollmentOptions { BootstrapEnabled=true }.Validate(host));
        Assert.Throws<InvalidOperationException>(() => new DeviceEnrollmentOptions { DevelopmentIssuerEnabled=true }.Validate(host));
        Assert.False(new UnconfiguredDeviceCertificateIssuer().Available);
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invite = await f.Invite(d); f.Options.BootstrapEnabled = false;
        Assert.Equal(HttpStatusCode.NotFound,(await f.Bootstrap.PostAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/receipt-challenge",null)).StatusCode);
        f.Options.BootstrapEnabled = true;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await f.Bootstrap.PostAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/challenge",new StringContent(new string('x',33000)))).StatusCode);
        for(var i=0;i<5;i++) await f.Challenge(invite,key);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/challenge",new EnrollmentChallengeInput(invite.InvitationSecret,EnrollmentFixture.Csr(key,d.Id)))).StatusCode);
    }
}
