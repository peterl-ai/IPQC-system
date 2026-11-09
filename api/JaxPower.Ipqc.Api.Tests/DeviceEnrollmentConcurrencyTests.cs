using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DeviceEnrollmentConcurrencyTests
{
    [Fact]
    public async Task Concurrent_invite_proof_and_approval_create_only_one_request_consumption_and_credential()
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var invites = await Task.WhenAll(Enumerable.Range(0,2).Select(_=>f.App.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/enrollments",new EnrollmentCreateInput(d.Revision))));
        Assert.Single(invites,x=>x.IsSuccessStatusCode); Assert.Single(invites,x=>x.StatusCode==HttpStatusCode.Conflict);
        var invite = (await invites.Single(x=>x.IsSuccessStatusCode).Content.ReadFromJsonAsync<EnrollmentInvitation>())!;
        var challenge = await f.Challenge(invite,key); var proof = EnrollmentFixture.Sign(challenge,key);
        var proofs = await Task.WhenAll(Enumerable.Range(0,2).Select(_=>f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/proof",proof)));
        Assert.Single(proofs,x=>x.IsSuccessStatusCode); Assert.Single(proofs,x=>x.StatusCode==HttpStatusCode.Conflict);
        var verified = await f.Get(d.Id); var before = await f.App.AuditCount(); var approval=f.Approval(d,verified);
        var approvals = await Task.WhenAll(Enumerable.Range(0,2).Select(_=>f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{verified.Id}/approve",approval)));
        Assert.All(approvals,x=>Assert.Equal(HttpStatusCode.Accepted,x.StatusCode));
        Assert.Equal(before+1,await f.App.AuditCount());
        Assert.Equal(1,await f.Db(db=>db.DeviceEnrollments.CountAsync())); Assert.Equal(1,await f.Db(db=>db.DeviceCredentials.CountAsync()));
        await Task.WhenAll(f.Process(),f.Process());
        Assert.Equal(1,f.Issuer.Issues); Assert.Equal("IssuedUnconfirmed",(await f.Get(d.Id)).State);
    }

    [Theory]
    [InlineData("cancel")] [InlineData("rebind")] [InlineData("unbind")] [InlineData("revoke")] [InlineData("replace")]
    public async Task Late_issuance_after_admin_change_is_retained_as_revoked_and_never_transferred(string action)
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var d = await f.BoundDevice(); var other = await f.App.LineAsync("B"); var next=await f.App.DeviceAsync("New PAD");
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256); var e=await f.Prove(await f.Invite(d),key); await f.Approve(d,e);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Issuer.BeforeIssue=async()=>{entered.SetResult();await release.Task;};
        var process=f.Process(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            if(action=="cancel") { var current=await f.Get(d.Id); (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{e.Id}/cancel",new EnrollmentCancelInput(current.Revision,"Cancel issuance"))).EnsureSuccessStatusCode(); }
            else if(action=="rebind") await f.App.DeviceAction(d,"rebind",new DeviceBindingInput(other.Id,d.Revision,"Move"));
            else if(action=="replace") await f.App.DeviceAction(d,"replace",new DeviceReplaceInput(next.Id,d.Revision,next.Revision,"Replace"));
            else await f.App.DeviceAction(d,action,new DeviceReasonInput(d.Revision,"Retire"));
        }
        finally {release.TrySetResult();}
        await process;
        if(action!="cancel")
        {
            var events=await f.Db(db=>db.ProvisioningAuditEvents.Where(x=>x.DeviceId==d.Id).ToListAsync());
            var cancelled=Assert.Single(events,x=>x.Action=="EnrollmentCancelled");
            Assert.Contains(events,x=>x.Action==(action switch {"rebind"=>"ReservationRebound","unbind"=>"ReservationUnbound","revoke"=>"DeviceRevoked",_=>"DeviceReplaced"})&&x.ChangeId==cancelled.ChangeId);
        }
        var final=await f.Get(d.Id); Assert.Equal("Cancelled",final.State); Assert.Equal("Revoked",final.Candidate!.State);
        Assert.NotNull(final.Candidate.CertificatePem); Assert.True(final.Candidate.ExternalRevocationPending);
        Assert.Equal(d.Id,await f.Db(async db=>(await db.DeviceCredentials.SingleAsync()).DeviceId));
        Assert.Equal(d.LineId,await f.Db(async db=>(await db.DeviceCredentials.SingleAsync()).LineId));
        Assert.False((await f.App.GetDevice(d.Id)).CanExecuteLineShared); Assert.Equal("NotEnrolled",(await f.App.GetDevice(next.Id)).CredentialStatus);
        f.Clock.Advance(TimeSpan.FromSeconds(6)); await f.Process();
        Assert.False((await f.Get(d.Id)).Candidate!.ExternalRevocationPending);
        Assert.True(File.Exists(Path.Combine(f.Options.ReceiptDirectory,final.Candidate.IssuanceOperationId.ToString("N")+".revoked")));
        Assert.Equal(1,f.Issuer.Issues);
    }

    [Fact]
    public async Task Lost_issuer_response_and_process_restart_recover_the_original_durable_certificate_without_resigning()
    {
        await using var f=await EnrollmentFixture.CreateAsync(); var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var e=await f.Approve(d,await f.Prove(await f.Invite(d),key));f.Issuer.LoseResponseOnce=true;
        await f.Process(); var unknown=await f.Get(d.Id);Assert.Equal("OutcomeUnknown",unknown.Candidate!.State);
        using var restarted=new DevelopmentDeviceCertificateIssuer(f.Options,new EnrollmentTestHost());
        var original=await restarted.RecoverAsync(e.Candidate!.IssuanceOperationId,default);Assert.Equal(IssuerOutcome.Issued,original.Outcome);
        f.Clock.Advance(TimeSpan.FromSeconds(6));await f.Process(); var recovered=await f.Get(d.Id);
        Assert.Equal(original.CertificatePem,recovered.Candidate!.CertificatePem);Assert.Equal(1,f.Issuer.Issues);
        Assert.Equal(e.Candidate.IssuanceOperationId,recovered.Candidate.IssuanceOperationId);
        Assert.Equal(d,await f.App.GetDevice(d.Id));
    }

    [Fact]
    public async Task Unknown_issuer_status_never_starts_a_second_signing_operation()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await f.Approve(d,await f.Prove(await f.Invite(d),key));f.Issuer.Unknown=true;
        await f.Process();f.Clock.Advance(TimeSpan.FromSeconds(6));await f.Process();
        Assert.Equal(0,f.Issuer.Issues);Assert.Equal("OutcomeUnknown",(await f.Get(d.Id)).Candidate!.State);
    }

    [Fact]
    public async Task Issued_certificate_with_invalid_key_or_profile_is_not_accepted_and_is_queued_for_revocation()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await f.Approve(d,await f.Prove(await f.Invite(d),key));
        f.Issuer.Transform=(_,_)=>new(IssuerOutcome.Issued,f.Issuer.Profile.RootCertificatePem);
        await f.Process();var failed=await f.Get(d.Id);Assert.Equal("Failed",failed.Candidate!.State);
        Assert.Null(failed.Candidate.CertificatePem);Assert.True(failed.Candidate.ExternalRevocationPending);
        Assert.Equal("InvalidIssuerResult",failed.Candidate.LastErrorCode);Assert.Equal(d,await f.App.GetDevice(d.Id));
    }

    [Theory]
    [InlineData("invite")] [InlineData("proof")] [InlineData("approve")] [InlineData("cancel")] [InlineData("replace")]
    public async Task Audit_failure_rolls_back_nonce_enrollment_candidate_and_provisioning_changes(string stage)
    {
        var failure=new EnrollmentAuditFailure();await using var f=await EnrollmentFixture.CreateAsync(failure);var d=await f.BoundDevice();
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EnrollmentInvitation? invite=null;EnrollmentChallengeOutput? challenge=null;EnrollmentOutput? e=null;
        if(stage!="invite") {invite=await f.Invite(d);challenge=await f.Challenge(invite,key);e=invite.Enrollment;}
        if(stage is "approve" or "cancel" or "replace") {var r=await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{e!.Id}/proof",EnrollmentFixture.Sign(challenge!,key));r.EnsureSuccessStatusCode();e=(await r.Content.ReadFromJsonAsync<EnrollmentOutput>())!;}
        if(stage is "cancel" or "replace") e=await f.Approve(d,e!);
        var next=stage=="replace" ? await f.App.DeviceAsync("Replacement") : null;
        var before=await f.State();failure.Action=stage=="replace"?"DeviceReplaced":null;failure.Armed=true;
        var response=stage switch {
            "invite"=>await f.App.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/enrollments",new EnrollmentCreateInput(d.Revision)),
            "proof"=>await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{e!.Id}/proof",EnrollmentFixture.Sign(challenge!,key)),
            "approve"=>await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{e!.Id}/approve",f.Approval(d,e)),
            "replace"=>await f.App.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/replace",new DeviceReplaceInput(next!.Id,d.Revision,next.Revision,"Replace")),
            _=>await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{e!.Id}/cancel",new EnrollmentCancelInput(e.Revision,"Cancel")) };
        Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);Assert.True(failure.Fired);Assert.Equal(before,await f.State());
    }

    [Fact]
    public async Task Audit_failure_after_signing_recovers_durable_result_after_lease_expiry()
    {
        var failure=new EnrollmentAuditFailure();await using var f=await EnrollmentFixture.CreateAsync(failure);var d=await f.BoundDevice();using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await f.Approve(d,await f.Prove(await f.Invite(d),key));
        f.Issuer.BeforeIssue=()=>{failure.Action="CredentialCandidateIssued";failure.Armed=true;return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Process());
        var unknown=await f.Get(d.Id);Assert.Equal("OutcomeUnknown",unknown.Candidate!.State);Assert.Null(unknown.Candidate.CertificatePem);
        f.Clock.Advance(TimeSpan.FromSeconds(31));await f.Process();
        Assert.Equal("IssuedUnconfirmed",(await f.Get(d.Id)).State);Assert.Equal(1,f.Issuer.Issues);
    }

    private sealed class EnrollmentAuditFailure : SaveChangesInterceptor
    {
        public bool Armed;public bool Fired;public string? Action;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Armed&&data.Context!.ChangeTracker.Entries<ProvisioningAuditEvent>().Any(x=>x.State==EntityState.Added&&(Action is null||x.Entity.Action==Action)))
            {Armed=false;Fired=true;throw new InvalidOperationException("Injected audit failure.");}
            return ValueTask.FromResult(result);
        }
    }
}
