using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class EnrollmentBindingTests
{
    [Theory]
    [InlineData("audience")] [InlineData("deviceId")] [InlineData("lineId")]
    [InlineData("bindingVersion")] [InlineData("targetBindingVersion")]
    [InlineData("csrSha256")] [InlineData("spkiSha256")] [InlineData("nonce")] [InlineData("challengeId")]
    public async Task Even_valid_key_cannot_sign_a_different_environment_identity_version_or_nonce(string claim)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invite=await f.Invite(d);var challenge=await f.Challenge(invite,key);
        var json=JsonNode.Parse(DeviceEnrollmentProof.Decode(challenge.Payload))!;
        if(claim.Contains("Version"))json[claim]=999;else json[claim]="different-bound-value";
        var forged=challenge with {Payload=DeviceEnrollmentProof.Encode(Encoding.UTF8.GetBytes(json.ToJsonString()))};var before=await f.State();
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/proof",EnrollmentFixture.Sign(forged,key))).StatusCode);
        Assert.Equal(before,await f.State());
    }

    [Fact]
    public async Task CSR_signature_tampering_stale_admin_snapshot_and_unavailable_issuer_leave_no_candidate()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invite=await f.Invite(d);var csr=Convert.FromBase64String(EnrollmentFixture.Csr(key,d.Id));csr[^1]^=1;var before=await f.State();
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/challenge",new EnrollmentChallengeInput(invite.InvitationSecret,Convert.ToBase64String(csr)))).StatusCode);
        Assert.Equal(before,await f.State());var proof=await f.Prove(invite,key);before=await f.State();
        var approval=f.Approval(d,proof);
        foreach(var wrong in new[]{approval with{ExpectedRevision=1},approval with{ExpectedDeviceRevision=d.Revision+1},approval with{LineId=Guid.NewGuid()},approval with{BindingVersion=d.BindingVersion+1},approval with{CsrSha256=new string('0',64)},approval with{SpkiSha256=new string('0',64)}})
            Assert.Equal(HttpStatusCode.Conflict,(await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{proof.Id}/approve",wrong)).StatusCode);
        f.Issuer.Available=false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{proof.Id}/approve",approval)).StatusCode);
        Assert.Equal(before,await f.State());Assert.Equal(0,f.Issuer.Issues);
    }

    [Fact]
    public async Task Environment_change_and_rebind_invalidate_old_proof_and_approval_without_moving_credentials()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invite=await f.Invite(d);var challenge=await f.Challenge(invite,key);var before=await f.State();var env=f.Options.EnvironmentId;f.Options.EnvironmentId="other-environment";
        Assert.Equal(HttpStatusCode.Conflict,(await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/proof",EnrollmentFixture.Sign(challenge,key))).StatusCode);Assert.Equal(before,await f.State());f.Options.EnvironmentId=env;
        var proof=await f.Prove(invite,key);var target=await f.App.LineAsync("OTHER");await f.App.DeviceAction(d,"rebind",new DeviceBindingInput(target.Id,d.Revision,"Move"));before=await f.State();
        Assert.Equal(HttpStatusCode.Conflict,(await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{proof.Id}/approve",f.Approval(d,proof))).StatusCode);
        Assert.Equal(before,await f.State());Assert.Equal("Cancelled",(await f.Get(d.Id)).State);Assert.Equal(0,await f.Db(db=>db.DeviceCredentials.CountAsync()));
    }

    [Fact]
    public async Task Expired_candidate_is_preserved_and_revoked_when_a_new_invitation_is_created()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var old=await f.Approve(d,await f.Prove(await f.Invite(d),key));await f.Process();
        f.Clock.Advance(TimeSpan.FromMinutes(31));
        // Authenticate again because the synthetic clock also controls legacy JWT lifetime.
        using var admin=await TestAccounts.ClientAsync(f.App.Factory,"admin");
        var response=await admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/enrollments",new EnrollmentCreateInput(d.Revision));response.EnsureSuccessStatusCode();
        await f.Db(async db=>{var history=await db.DeviceEnrollments.SingleAsync(x=>x.Id==old.Id);Assert.Equal(JaxPower.Ipqc.Api.Domain.EnrollmentState.Expired,history.State);var k=await db.DeviceCredentials.SingleAsync();Assert.Equal(old.Candidate!.Id,k.Id);Assert.Equal(JaxPower.Ipqc.Api.Domain.CandidateCredentialState.Revoked,k.State);Assert.True(k.ExternalRevocationPending);Assert.NotNull(k.CertificatePem);return 0;});
    }
}
