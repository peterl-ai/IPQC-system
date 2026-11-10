using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DeviceProvisioningRetirementTests
{
    internal static (HttpMethod Method,string Path)[] Routes(Guid id) => [
        (HttpMethod.Post,"/api/admin/devices"),(HttpMethod.Put,$"/api/admin/devices/{id}"),
        ..new[]{"bind","rebind","unbind","replace"}.Select(x=>(HttpMethod.Post,$"/api/admin/devices/{id}/{x}")),
        (HttpMethod.Post,$"/api/admin/devices/{id}/enrollments"),(HttpMethod.Post,$"/api/admin/enrollments/{id}/approve")];

    [Theory]
    [InlineData("admin",410)] [InlineData("anonymous",401)] [InlineData("invalid",401)]
    [InlineData("revoked",401)] [InlineData("pqe",403)] [InlineData("dev-ipqa-1",403)] [InlineData("forced",403)]
    public async Task Retired_Admin_routes_authorize_before_body_target_or_retirement(string who,int status)
    {
        await using var f=await ProvisioningFixture.CreateAsync();var d=await f.DeviceAsync("History");
        using var client=who is "anonymous" or "invalid"?f.Factory.CreateClient():await TestAccounts.ClientAsync(f.Factory,who is "forced" or "revoked"?"admin":who);
        if(who=="invalid")client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","invalid");
        if(who is "forced" or "revoked")await f.Db(async db=>{var u=await db.ApplicationUsers.SingleAsync(x=>x.Id==TestAccounts.AdminId);if(who=="forced")u.MustChangePassword=true;else u.AuthVersion++;await db.SaveChangesAsync();return 0;});
        var before=await f.StateAsync();
        foreach(var id in new[]{d.Id,Guid.NewGuid()})foreach(var (method,path) in Routes(id))
        {
            using var req=new HttpRequestMessage(method,path){Content=new StringContent("{malformed "+new string('x',33000),Encoding.UTF8,"application/json")};
            var response=await client.SendAsync(req);Assert.Equal(status,(int)response.StatusCode);
            if(status==410)Assert.Equal(DeviceProvisioningRetirement.Code,(await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal(before,await f.StateAsync());
    }
    [Fact]
    public async Task Expired_personal_JWT_still_returns_401_on_retired_routes()
    {
        var clock=new EnrollmentClock();
        await using var f=await ProvisioningFixture.CreateAsync(configure:services=>{services.RemoveAll<AuthenticationClock>();services.AddSingleton(new AuthenticationClock(clock));});
        clock.Advance(TimeSpan.FromHours(1));
        foreach(var (method,path) in Routes(Guid.NewGuid()))Assert.Equal(HttpStatusCode.Unauthorized,(await f.Admin.SendAsync(new(method,path))).StatusCode);
    }
    [Theory]
    [InlineData("Testing")] [InlineData("Development")] [InlineData("Production")]
    public async Task Legacy_flags_and_spoofed_transport_cannot_reopen_any_retired_surface(string environment)
    {
        await using var f=await ProvisioningFixture.CreateAsync(environment:environment,settings:new(){
            ["Enrollment:BootstrapEnabled"]="true",["Enrollment:DevelopmentIssuerEnabled"]="true",["Enrollment:WorkerEnabled"]="true",
            ["Enrollment:RootPfxPath"]="unused-invalid-path",["Enrollment:ReceiptDirectory"]="unused-invalid-directory"});
        Assert.NotNull(await f.LineAsync("ACTIVE"));
        f.Admin.DefaultRequestHeaders.Add("X-Device-Id",Guid.NewGuid().ToString());
        f.Admin.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");
        foreach(var (method,path) in Routes(Guid.NewGuid()))Assert.Equal(HttpStatusCode.Gone,(await f.Admin.SendAsync(new(method,path){Content=JsonContent.Create(new{status="Trusted"})})).StatusCode);
        var before=await f.StateAsync();
        foreach(var action in new[]{"challenge","proof","receipt-challenge","receipt"})
            foreach(var id in new[]{Guid.NewGuid(),Guid.Empty})
                Assert.Equal(HttpStatusCode.NotFound,(await f.Admin.PostAsync($"/bootstrap/v1/enrollments/{id}/{action}",new StringContent("{invalid"))).StatusCode);
        Assert.Equal(before,await f.StateAsync());
        Assert.IsType<UnconfiguredDeviceCertificateIssuer>(f.Factory.Services.GetRequiredService<IDeviceCertificateIssuer>());
        using var scope=f.Factory.Services.CreateScope();Assert.False(await scope.ServiceProvider.GetRequiredService<DeviceCredentialIssuance>().ProcessNextAsync(default));
        new DeviceEnrollmentOptions{BootstrapEnabled=true,DevelopmentIssuerEnabled=true}.Validate(new EnrollmentTestHost{EnvironmentName="Production"});
    }
    [Fact]
    public async Task Direct_services_reject_before_input_validation_database_and_signer_access()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var before=await f.State();
        using var scope=f.App.Factory.Services.CreateScope();
        var d=scope.ServiceProvider.GetRequiredService<DeviceRegistryService>();var e=scope.ServiceProvider.GetRequiredService<DeviceEnrollmentService>();
        Func<Task>[] operations=[()=>d.CreateAsync(null!,Guid.Empty,"",default),()=>d.UpdateAsync(Guid.Empty,null!,Guid.Empty,"",default),
            ()=>d.ReserveAsync(Guid.Empty,null!,false,Guid.Empty,"",default),()=>d.ReserveAsync(Guid.Empty,null!,true,Guid.Empty,"",default),
            ()=>d.ReleaseAsync(Guid.Empty,null!,false,Guid.Empty,"",default),()=>d.ReplaceAsync(Guid.Empty,null!,Guid.Empty,"",default),
            ()=>e.CreateAsync(Guid.Empty,null!,Guid.Empty,"",default),()=>e.ApproveAsync(Guid.Empty,null!,Guid.Empty,"",default),
            ()=>e.ChallengeAsync(Guid.Empty,null!,default),()=>e.ProofAsync(Guid.Empty,null!,default),
            ()=>e.ReceiptChallengeAsync(Guid.Empty,default),()=>e.ReceiptAsync(Guid.Empty,null!,default),
            ()=>scope.ServiceProvider.GetRequiredService<IDeviceCertificateIssuer>().IssueAsync(null!,default)];
        foreach(var op in operations){var ex=await Assert.ThrowsAsync<ProvisioningException>(op);Assert.Equal(410,ex.Status);Assert.Equal(DeviceProvisioningRetirement.Code,ex.Message);}
        Assert.Equal(before,await f.State());Assert.Equal(0,f.Issuer.Issues);
    }
    [Fact]
    public async Task Old_valid_proof_and_candidate_receipt_are_not_an_anonymous_recovery_or_trust_channel()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d,credentialState:JaxPower.Ipqc.Api.Domain.CandidateCredentialState.IssuedUnconfirmed);
        var before=await f.State();
        foreach(var action in new[]{"challenge","proof","receipt-challenge","receipt"})
            Assert.Equal(HttpStatusCode.NotFound,(await f.Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{old.Id}/{action}",new{invitationSecret="historical",jws="old.proof.signature",challengeId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(before,await f.State());Assert.Empty(f.Issuer.Revocations);Assert.Equal(0,f.Issuer.Issues);
        Assert.False((await f.Get(d.Id)).CanExecuteLineShared);Assert.False((await f.App.GetDevice(d.Id)).CanExecuteLineShared);
    }
}
