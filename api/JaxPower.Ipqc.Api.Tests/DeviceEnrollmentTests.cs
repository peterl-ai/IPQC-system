using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DeviceEnrollmentTests
{
    [Fact]
    public async Task Expired_projection_is_read_only_and_still_accepts_explicit_Admin_cancellation()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d,credentialState:CandidateCredentialState.IssuedUnconfirmed);
        f.Clock.Advance(TimeSpan.FromMinutes(31));var before=await f.State();var output=await f.Get(d.Id);
        Assert.Equal("Expired",output.State);Assert.Equal(before,await f.State());Assert.Equal("IssuedUnconfirmed",output.Candidate!.State);
        var reply=await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{old.Id}/cancel",new EnrollmentCancelInput(output.Revision,"Close expired history"));
        reply.EnsureSuccessStatusCode();Assert.Equal("Cancelled",(await f.Get(d.Id)).State);
        Assert.True((await f.Candidate()).ExternalRevocationPending);
        Assert.Equal(d,await f.App.GetDevice(d.Id));
    }
    [Theory]
    [InlineData("anonymous",401)] [InlineData("pqe",403)] [InlineData("dev-ipqa-1",403)] [InlineData("forced",403)]
    public async Task Historical_reads_and_cancellation_remain_Admin_only(string who,int status)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d);
        if(who=="forced")await f.Db(async db=>{var user=await db.ApplicationUsers.SingleAsync(x=>x.Id==TestAccounts.AdminId);user.MustChangePassword=true;await db.SaveChangesAsync();return 0;});
        using var client=who=="anonymous"?f.App.Factory.CreateClient():await TestAccounts.ClientAsync(f.App.Factory,who=="forced"?"admin":who);
        var before=await f.State();
        Assert.Equal(status,(int)(await client.GetAsync($"/api/admin/devices/{d.Id}/enrollments")).StatusCode);
        Assert.Equal(status,(int)(await client.PostAsync($"/api/admin/enrollments/{old.Id}/cancel",new StringContent(new string('x',33000),Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(before,await f.State());
    }
    [Fact]
    public async Task Cancellation_preserves_validation_revision_environment_and_no_store_contracts()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d);
        var get=await f.App.Admin.GetAsync($"/api/admin/devices/{d.Id}/enrollments");Assert.True(get.Headers.CacheControl!.NoStore);
        var before=await f.State();
        foreach(var (id,input,status) in new[]{(old.Id,new EnrollmentCancelInput(1," "),400),(old.Id,new EnrollmentCancelInput(2,"Reason"),409),(Guid.NewGuid(),new EnrollmentCancelInput(1,"Reason"),404)})
            Assert.Equal(status,(int)(await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{id}/cancel",input)).StatusCode);
        f.Options.EnvironmentId="wrong";Assert.Equal(HttpStatusCode.Conflict,(await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{old.Id}/cancel",new EnrollmentCancelInput(1,"Reason"))).StatusCode);
        Assert.Equal(before,await f.State());
    }
    [Theory]
    [InlineData("der")] [InlineData("short")] [InlineData("long")] [InlineData("algorithm")] [InlineData("payload")] [InlineData("padding")] [InlineData("wrong-key")]
    public void Historical_wire_validation_remains_strict_without_reopening_HTTP(string attack)
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var wrong=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes=Encoding.UTF8.GetBytes("historical-test");var challenge=new EnrollmentChallengeOutput(Guid.NewGuid(),DeviceEnrollmentProof.ProtectedHeader,DeviceEnrollmentProof.Encode(bytes),DateTime.UtcNow);
        var proof=EnrollmentFixture.Sign(challenge,attack=="wrong-key"?wrong:key,attack=="der"?DSASignatureFormat.Rfc3279DerSequence:DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var parts=proof.Jws.Split('.');
        if(attack=="short")parts[2]=DeviceEnrollmentProof.Encode(new byte[63]);if(attack=="long")parts[2]=DeviceEnrollmentProof.Encode(new byte[65]);
        if(attack=="algorithm")parts[0]=DeviceEnrollmentProof.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        if(attack=="payload")parts[1]=DeviceEnrollmentProof.Encode(Encoding.UTF8.GetBytes("altered"));if(attack=="padding")parts[2]+="=";
        Assert.Throws<ProvisioningException>(()=>DeviceEnrollmentProof.Verify(string.Join('.',parts),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),DeviceEnrollmentProof.Hash(bytes)));
        Assert.Equal(bytes,DeviceEnrollmentProof.Verify(EnrollmentFixture.Sign(challenge,key).Jws,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),DeviceEnrollmentProof.Hash(bytes)));
    }
}
