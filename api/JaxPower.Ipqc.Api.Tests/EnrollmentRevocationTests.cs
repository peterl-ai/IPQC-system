using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class EnrollmentRevocationTests
{
    [Theory]
    [InlineData("cancel")] [InlineData("revoke")] [InlineData("expire")]
    [InlineData("retirement")] [InlineData("historical-rebind")] [InlineData("historical-replacement")]
    public async Task Failed_no_PEM_pending_survives_every_closure_failure_and_acknowledges_original_operation(string action)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();
        var old=await f.Seed(d,credentialState:CandidateCredentialState.Failed,pending:true,receipt:true);
        var identity=Identity(await f.Candidate());var history=await f.Db(db=>db.ProvisioningAuditEvents.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>JsonSerializer.Serialize(x)));
        if(action is "historical-rebind" or "historical-replacement")await f.Db(async db=>{
            var device=await db.RegisteredDevices.SingleAsync(x=>x.Id==d.Id);var e=await db.DeviceEnrollments.SingleAsync();
            // Seed a pre-retirement changed reservation; no retired application action is reopened.
            device.LineId=null;device.BindingVersion++;device.Revision++;e.State=EnrollmentState.Cancelled;e.TerminalReason=action;
            await db.SaveChangesAsync();return 0;});
        if(action=="cancel") (await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{old.Id}/cancel",new EnrollmentCancelInput(1,"Close old failure"))).EnsureSuccessStatusCode();
        if(action=="revoke")await f.App.DeviceAction(d,"revoke",new DeviceReasonInput(d.Revision,"Retire"));
        if(action=="expire")f.Clock.Advance(TimeSpan.FromMinutes(31));
        f.Issuer.FailRevocation=true;Assert.True(await f.Process());
        Assert.True((await f.Candidate()).ExternalRevocationPending);Assert.Equal(identity,Identity(await f.Candidate()));
        Assert.Equal(new[]{old.OperationId!.Value},f.Issuer.Revocations);Assert.Empty(f.Issuer.RevocationAcknowledgements);
        Assert.Equal(0,await Acknowledgements(f));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Issuer.FailRevocation=false;f.Issuer.BeforeRevoke=async()=>{entered.SetResult();await release.Task;};f.Clock.Advance(TimeSpan.FromSeconds(6));
        var cleanup=f.Process();try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));Assert.True((await f.Candidate()).ExternalRevocationPending);Assert.Equal(0,await Acknowledgements(f));}finally{release.TrySetResult();}
        Assert.True(await cleanup);Assert.False((await f.Candidate()).ExternalRevocationPending);Assert.Equal(identity,Identity(await f.Candidate()));
        Assert.Equal(1,await Acknowledgements(f));Assert.Equal(2,f.Issuer.Revocations.Count);Assert.Equal(0,f.Issuer.Issues);
        f.Clock.Advance(TimeSpan.FromSeconds(6));Assert.False(await f.Process());Assert.Equal(2,f.Issuer.Revocations.Count);
        await f.Db(async db=>{foreach(var entry in history)Assert.Equal(entry.Value,JsonSerializer.Serialize(await db.ProvisioningAuditEvents.SingleAsync(x=>x.Id==entry.Key)));return 0;});
    }
    [Fact]
    public async Task Audit_failure_after_signer_ack_rolls_back_pending_then_retries_same_operation_once()
    {
        var failure=new RevocationAuditFailure();await using var f=await EnrollmentFixture.CreateAsync(failure);var d=await f.BoundDevice();
        var old=await f.Seed(d,credentialState:CandidateCredentialState.Failed,pending:true,receipt:true);failure.Armed=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Process());Assert.True(failure.Fired);
        Assert.True((await f.Candidate()).ExternalRevocationPending);Assert.Equal(0,await Acknowledgements(f));Assert.Single(f.Issuer.RevocationAcknowledgements);
        f.Clock.Advance(TimeSpan.FromSeconds(31));Assert.True(await f.Process());Assert.False((await f.Candidate()).ExternalRevocationPending);
        Assert.Equal(new[]{old.OperationId!.Value,old.OperationId.Value},f.Issuer.Revocations);Assert.Equal(1,await Acknowledgements(f));
    }
    [Fact]
    public async Task Failure_to_commit_closure_rolls_back_all_challenges_states_and_audit_before_external_work()
    {
        var failure=new FailProvisioningAudit();await using var f=await EnrollmentFixture.CreateAsync(failure);var d=await f.BoundDevice();
        await f.Seed(d,credentialState:CandidateCredentialState.IssuedUnconfirmed);var before=await f.State();failure.Armed=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Process());Assert.Equal(before,await f.State());Assert.Empty(f.Issuer.Revocations);
    }
    [Theory]
    [InlineData("cancel")] [InlineData("revoke")]
    public async Task Retained_Admin_closure_audit_failure_rolls_back_enrollment_credential_challenges_and_device(string action)
    {
        var failure=new FailProvisioningAudit();await using var f=await EnrollmentFixture.CreateAsync(failure);var d=await f.BoundDevice();
        var old=await f.Seed(d,credentialState:CandidateCredentialState.IssuedUnconfirmed);var before=await f.State();failure.Armed=true;
        var response=action=="cancel"?await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{old.Id}/cancel",new EnrollmentCancelInput(1,"Close")):
            await f.App.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke",new DeviceReasonInput(d.Revision,"Retire"));
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError,response.StatusCode);Assert.True(failure.Fired);
        Assert.Equal(before,await f.State());Assert.Empty(f.Issuer.Revocations);
    }
    internal static string Identity(DeviceCredential x)=>JsonSerializer.Serialize(new{x.Id,x.EnrollmentId,x.DeviceId,x.LineId,x.BindingVersion,x.IssuanceOperationId,x.IssuerId,x.RootCertificatePem,x.SpkiBase64,x.NotBeforeUtc,x.NotAfterUtc,x.CertificatePem,x.CertificateSha256,x.IssuedAtUtc});
    internal static Task<int> Acknowledgements(EnrollmentFixture f)=>f.Db(db=>db.ProvisioningAuditEvents.CountAsync(x=>x.Action=="CredentialRevocationRecorded"));
    private sealed class RevocationAuditFailure:SaveChangesInterceptor
    {
        public bool Armed;public bool Fired;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {if(Armed&&data.Context!.ChangeTracker.Entries<ProvisioningAuditEvent>().Any(x=>x.State==EntityState.Added&&x.Entity.Action=="CredentialRevocationRecorded")){Armed=false;Fired=true;throw new InvalidOperationException("Injected acknowledgement audit failure.");}return ValueTask.FromResult(result);}
    }
}
