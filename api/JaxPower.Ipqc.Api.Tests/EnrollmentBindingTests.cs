using System.Security.Cryptography;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class EnrollmentBindingTests
{
    [Theory]
    [InlineData(EnrollmentState.Invited,null,false)]
    [InlineData(EnrollmentState.Approved,CandidateCredentialState.PendingIssuance,false)]
    [InlineData(EnrollmentState.IssuedUnconfirmed,CandidateCredentialState.IssuedUnconfirmed,false)]
    [InlineData(EnrollmentState.Issuing,CandidateCredentialState.OutcomeUnknown,false)]
    [InlineData(EnrollmentState.Cancelled,CandidateCredentialState.Failed,true)]
    [InlineData(EnrollmentState.Expired,CandidateCredentialState.Revoked,true)]
    public async Task Drain_covers_preexisting_states_and_never_activates_or_signs(EnrollmentState state,CandidateCredentialState? candidate,bool pending)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();
        await f.Seed(d,state,candidate,pending,receipt:candidate is not null);var identity=candidate is null?null:EnrollmentRevocationTests.Identity(await f.Candidate());
        Assert.True(await f.Process());Assert.False(await f.Process());
        var output=await f.Get(d.Id);Assert.Contains(output.State,new[]{"Cancelled","Expired"});Assert.False(output.CanExecuteLineShared);
        Assert.Equal(d,await f.App.GetDevice(d.Id));Assert.Equal(0,f.Issuer.Issues);
        if(candidate is not null){Assert.Equal(identity,EnrollmentRevocationTests.Identity(await f.Candidate()));Assert.False((await f.Candidate()).ExternalRevocationPending);}
        Assert.True(await f.Db(db=>db.EnrollmentChallenges.AllAsync(x=>x.ConsumedAtUtc!=null))||state is EnrollmentState.Cancelled or EnrollmentState.Expired);
    }
    [Fact]
    public async Task Cleanup_disabled_and_wrong_environment_leave_all_records_untouched()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown);
        var before=await f.State();f.Options.CleanupEnabled=false;Assert.False(await f.Process());Assert.Equal(before,await f.State());
        f.Options.CleanupEnabled=true;f.Options.EnvironmentId="another";Assert.False(await f.Process());Assert.Equal(before,await f.State());Assert.Empty(f.Issuer.Revocations);
    }
    [Theory]
    [InlineData("unavailable")] [InlineData("unknown")] [InlineData("issuer-mismatch")] [InlineData("root-mismatch")]
    public async Task Missing_or_mismatched_original_issuer_never_fabricates_acknowledgement(string issue)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,pending:true);
        if(issue=="unavailable")f.Issuer.Available=false;if(issue=="unknown")f.Issuer.Unknown=true;
        if(issue=="issuer-mismatch")await f.Db(async db=>{(await db.DeviceCredentials.SingleAsync()).IssuerId="wrong-original-issuer";await db.SaveChangesAsync();return 0;});
        if(issue=="root-mismatch")await f.Db(async db=>{(await db.DeviceCredentials.SingleAsync()).RootCertificatePem="wrong-original-root";await db.SaveChangesAsync();return 0;});
        Assert.True(await f.Process());Assert.True((await f.Candidate()).ExternalRevocationPending);Assert.Empty(f.Issuer.Revocations);Assert.Equal(0,await EnrollmentRevocationTests.Acknowledgements(f));Assert.Equal(0,f.Issuer.Issues);
    }
    [Theory]
    [InlineData(IssuerOutcome.NotIssued)] [InlineData(IssuerOutcome.Unknown)] [InlineData(IssuerOutcome.Issued)]
    public async Task Recover_result_cannot_start_issuance_and_only_acknowledged_cancellation_clears_pending(IssuerOutcome result)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,pending:true,receipt:true);
        f.Issuer.ForcedOutcome=result;Assert.True(await f.Process());Assert.Equal(0,f.Issuer.Issues);
        Assert.Equal(result==IssuerOutcome.Unknown,(await f.Candidate()).ExternalRevocationPending);
        Assert.Equal(result==IssuerOutcome.Unknown?0:1,f.Issuer.RevocationAcknowledgements.Count);
    }
    [Fact]
    public async Task Missing_original_journal_receipt_remains_unknown_and_is_not_recreated_as_success()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,pending:true);
        using var cleanup=new DevelopmentDeviceCertificateIssuer(f.Options,new EnrollmentTestHost(),cleanupOnly:true);
        Assert.Equal(IssuerOutcome.Unknown,(await cleanup.RecoverAsync(old.OperationId!.Value,default)).Outcome);
        await Assert.ThrowsAsync<IOException>(()=>cleanup.RevokeAsync(old.OperationId.Value,default));
        await Assert.ThrowsAsync<ProvisioningException>(()=>cleanup.IssueAsync(null!,default));
        Assert.False(File.Exists(Path.Combine(f.Options.ReceiptDirectory,old.OperationId.Value.ToString("N")+".revoked")));
        var bad=new DeviceEnrollmentOptions{CleanupEnabled=true,EnvironmentId=f.Options.EnvironmentId,DevelopmentIssuerEnabled=true,
            RootPfxPath=f.Options.RootPfxPath,RootPassword=f.Options.RootPassword,ReceiptDirectory=Path.Combine(f.DirectoryPath,"missing-original-journal")};
        Assert.Throws<IOException>(()=>new DevelopmentDeviceCertificateIssuer(bad,new EnrollmentTestHost(),cleanupOnly:true));
        Assert.False(Directory.Exists(bad.ReceiptDirectory));
    }
    [Fact]
    public async Task Expired_original_certificate_can_be_revoked_without_revalidating_it_as_current_authority()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d,credentialState:CandidateCredentialState.IssuedUnconfirmed);
        var identity=EnrollmentRevocationTests.Identity(await f.Candidate());f.Clock.Advance(TimeSpan.FromDays(31));
        Assert.True(await f.Process());Assert.False((await f.Candidate()).ExternalRevocationPending);Assert.Equal(identity,EnrollmentRevocationTests.Identity(await f.Candidate()));Assert.Equal(0,f.Issuer.Issues);
    }
    [Fact]
    public void Historical_CSR_curve_subject_and_signature_validation_is_preserved()
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var unsupported=ECDsa.Create(ECCurve.NamedCurves.nistP384);var id=Guid.NewGuid();
        var valid=EnrollmentFixture.Csr(key,id);Assert.NotEmpty(DeviceEnrollmentProof.ReadCsr(valid,id).Spki);
        var tampered=Convert.FromBase64String(valid);tampered[^1]^=1;
        foreach(var input in new[]{EnrollmentFixture.Csr(unsupported,id),EnrollmentFixture.Csr(key,Guid.NewGuid()),Convert.ToBase64String(tampered),"not-a-csr"})
            Assert.Throws<ProvisioningException>(()=>DeviceEnrollmentProof.ReadCsr(input,id));
    }
}
