using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DeviceEnrollmentConcurrencyTests
{
    [Fact]
    public async Task Two_workers_cannot_acknowledge_the_same_live_lease()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,receipt:true);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Issuer.BeforeRevoke=async()=>{entered.SetResult();await release.Task;};
        var first=f.Process();try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));Assert.False(await f.Process());Assert.True((await f.Candidate()).ExternalRevocationPending);}finally{release.TrySetResult();}
        Assert.True(await first);Assert.Single(f.Issuer.Revocations);Assert.Equal(1,await EnrollmentRevocationTests.Acknowledgements(f));Assert.Equal(0,f.Issuer.Issues);
    }
    [Fact]
    public async Task Expired_lease_late_acknowledgement_cannot_finalize_the_new_workers_operation()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,receipt:true);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var count=0;
        f.Issuer.BeforeRevoke=async()=>{if(Interlocked.Increment(ref count)==1){entered.SetResult();await release.Task;}};
        var first=f.Process();try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));f.Clock.Advance(TimeSpan.FromSeconds(31));Assert.True(await f.Process());Assert.False((await f.Candidate()).ExternalRevocationPending);}finally{release.TrySetResult();}
        await first;Assert.Equal(2,f.Issuer.Revocations.Count);Assert.Equal(1,await EnrollmentRevocationTests.Acknowledgements(f));
    }
    [Theory]
    [InlineData("cancel")] [InlineData("revoke")]
    public async Task Cleanup_racing_Admin_closure_preserves_one_terminal_identity(string action)
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,receipt:true);
        var identity=EnrollmentRevocationTests.Identity(await f.Candidate());
        var process=f.Process();var response=action=="cancel"?await f.App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{old.Id}/cancel",new EnrollmentCancelInput(1,"Close")):
            await f.App.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/revoke",new DeviceReasonInput(d.Revision,"Retire"));
        Assert.Contains(response.StatusCode,new[]{HttpStatusCode.OK,HttpStatusCode.Conflict});await process;
        f.Clock.Advance(TimeSpan.FromSeconds(31));await f.Process();Assert.False((await f.Candidate()).ExternalRevocationPending);
        Assert.Equal(identity,EnrollmentRevocationTests.Identity(await f.Candidate()));Assert.Equal(1,await EnrollmentRevocationTests.Acknowledgements(f));
    }
    [Fact]
    public async Task Unknown_or_failing_first_work_does_not_starve_a_queue_larger_than_the_worker_budget()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var ids=new List<Guid>();
        for(var i=0;i<13;i++){var d=await f.BoundDevice("L"+i);ids.Add((await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,receipt:true)).OperationId!.Value);}
        f.Issuer.FailedOperations.Add(ids[0]);
        for(var i=0;i<8;i++)Assert.True(await f.Process());f.Clock.Advance(TimeSpan.FromSeconds(6));
        for(var i=0;i<8;i++)await f.Process();
        Assert.Equal(13,f.Issuer.Revocations.Distinct().Count());
        Assert.Equal(1,await f.Db(db=>db.DeviceCredentials.CountAsync(x=>x.ExternalRevocationPending)));Assert.Equal(0,f.Issuer.Issues);
    }
    [Fact]
    public async Task Cancellation_during_signer_call_leaves_original_operation_recoverable()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();var old=await f.Seed(d,credentialState:CandidateCredentialState.OutcomeUnknown,receipt:true);
        using var ct=new CancellationTokenSource();f.Issuer.BeforeRevoke=()=>{ct.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Process(ct.Token));Assert.True((await f.Candidate()).ExternalRevocationPending);
        f.Issuer.BeforeRevoke=null;f.Clock.Advance(TimeSpan.FromSeconds(31));Assert.True(await f.Process());
        Assert.False((await f.Candidate()).ExternalRevocationPending);Assert.All(f.Issuer.Revocations,id=>Assert.Equal(old.OperationId,id));
    }
}
