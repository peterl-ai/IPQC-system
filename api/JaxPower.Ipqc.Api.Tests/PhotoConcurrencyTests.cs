using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoConcurrencyTests
{
    [Theory][InlineData(true,"explicit")][InlineData(false,"explicit")][InlineData(true,"reset")][InlineData(false,"reset")]
    [InlineData(true,"role")][InlineData(false,"role")][InlineData(true,"deactivate")][InlineData(false,"deactivate")]
    public async Task Source_revocation_both_orders(bool first,string action) {await using var p=await PhotoFixture.Create();await Security(p,first,action);}
    internal static async Task Security(PhotoFixture p,bool revokeFirst,string action)
    {
        var f=p.F;var s=await SharedDraftFixture.Setup(f);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));var admin=await f.Login("admin");
        async Task Revoke() {
            var legacy=f.Client(f.Token(admin,cs=>cs.Where(c=>c.Type!="personal_session_id").ToList()));
            var r=action switch {
                "explicit"=>await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke",new WorkContextRevokeInput(1,"Synthetic photo race")),
                "reset"=>await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password",new ResetPasswordInput("SyntheticChanged123A")),
                "role"=>await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}",new UserUpdateInput("Synthetic inspector","PQE")),
                _=>await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate",null)};
            Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());
        }
        var staged=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var continueStage=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(revokeFirst) p.Fault.AfterStage=async()=>{staged.TrySetResult();await continueStage.Task.WaitAsync(TimeSpan.FromSeconds(20));};
        var fragment=revokeFirst?(action=="explicit"?"UPDATE \"WorkContexts\"":"UPDATE \"ApplicationUsers\""):"INSERT INTO \"AttachmentAcceptances\"";
        var holder=0;var waiter=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync=(command,_)=> {
            if(command.Connection is NpgsqlConnection connection && command.Transaction is not null) {
                if(command.CommandText.Contains(fragment,StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder,connection.ProcessID,0);
                else if(holder!=0 && connection.ProcessID!=holder && command.CommandText.Contains("FOR ",StringComparison.Ordinal)) waiter.TrySetResult(connection.ProcessID);
            }return Task.CompletedTask;
        };
        f.Fault.Arm(fragment);var upload=PhotoFixture.Upload(s,a.AttachmentId,bytes);Task? revoke=null;
        try {
            if(revokeFirst) {await staged.Task.WaitAsync(TimeSpan.FromSeconds(15));revoke=Revoke();}
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if(revokeFirst) continueStage.TrySetResult();else revoke=Revoke();
            if(f.PostgresRoot is not null) {
                var pid=await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10));await using var monitor=new NpgsqlConnection(f.Connection);await monitor.OpenAsync();
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while(true) {
                    await using var query=new NpgsqlCommand("SELECT @holder = ANY(pg_blocking_pids(@waiter))",monitor);query.Parameters.AddWithValue("holder",holder);query.Parameters.AddWithValue("waiter",pid);
                    if((bool)(await query.ExecuteScalarAsync(deadline.Token))!) break;await Task.Delay(20,deadline.Token);
                }
                Assert.False(upload.IsCompleted);Assert.False(revoke!.IsCompleted);
            }
        } finally {continueStage.TrySetResult();f.Fault.Release.TrySetResult();}
        var result=await upload;await revoke!;Assert.Equal(revokeFirst?HttpStatusCode.Conflict:HttpStatusCode.OK,result.StatusCode);
        Assert.Equal(revokeFirst?0:1,await f.Read(db=>db.AttachmentAcceptances.CountAsync()));
        if(!revokeFirst) Assert.True((await s.Client().GetFromJsonAsync<AttachmentOutput>(PhotoFixture.Root(s)+$"/{a.AttachmentId}"))!.IsReady);
        await s.AssertCounts(0,0,0,0);
    }
    [Fact] public async Task Expired_writer_is_fenced() {await using var p=await PhotoFixture.Create();await Lease(p);}
    internal static async Task Lease(PhotoFixture p)
    {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.Fault.AfterStage=async()=>{entered.TrySetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(20));};var old=PhotoFixture.Upload(s,a.AttachmentId,bytes);
        AttachmentOutput? ready=null;
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Recover(s,a.AttachmentId)).StatusCode);
            p.F.Clock.Now=p.F.Clock.Now.AddMinutes(11);
            var request=new HttpRequestMessage(HttpMethod.Post,PhotoFixture.Root(s)+$"/{a.AttachmentId}/recover");request.Headers.Add("X-WorkContext-Proof",s.Source.ProofSecret);
            var r=await s.Client(s.A).SendAsync(request);Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());ready=await r.Content.ReadFromJsonAsync<AttachmentOutput>();
        } finally {release.TrySetResult();}
        Assert.Equal(HttpStatusCode.Conflict,(await old).StatusCode);Assert.Equal(s.A.User.Id,ready!.UploadedByUserId);Assert.Equal(s.A.PersonalSessionId,ready.UploadedPersonalSessionId);
        Assert.Equal(ready,await s.Client().GetFromJsonAsync<AttachmentOutput>(PhotoFixture.Root(s)+$"/{a.AttachmentId}"));Assert.Equal(1,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
    }
    [Fact] public async Task B_revoked_after_stream_is_not_accepted() {await using var p=await PhotoFixture.Create();await BRevoked(p);}
    internal static async Task BRevoked(PhotoFixture p) {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));
        p.Fault.AfterStage=async()=>{(await s.Client().PostAsJsonAsync("/api/auth/logout",new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();};
        Assert.Equal(HttpStatusCode.Unauthorized,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
        p.F.Clock.Now=p.F.Clock.Now.AddMinutes(11);s=s with{B=await p.F.Login("dev-ipqa-2")};var r=await PhotoFixture.Recover(s,a.AttachmentId);Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Cross_task_client_identity_race_is_a_conflict() {await using var p=await PhotoFixture.Create();await ClientIdentity(p);}
    [Fact] public async Task Expired_unclaimed_lease_cannot_commit() {await using var p=await PhotoFixture.Create();await Expiry(p);}
    internal static async Task Expiry(PhotoFixture p) {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));
        p.Fault.AfterStage=()=>{p.F.Clock.Now=p.F.Clock.Now.AddMinutes(11);return Task.CompletedTask;};
        Assert.Equal(HttpStatusCode.Conflict,(await PhotoFixture.Upload(s,a.AttachmentId,bytes)).StatusCode);Assert.Equal(0,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
        Assert.True((await PhotoFixture.Recover(s,a.AttachmentId)).IsSuccessStatusCode);
    }
    internal static async Task ClientIdentity(PhotoFixture p) {
        var f=p.F;var first=await SharedDraftFixture.Setup(f);f.Clock.Now=f.Clock.Now.AddHours(12);Assert.Equal(1,await LineSharedPlanningFixture.Generate(f));
        var secondTask=await f.Read(db=>db.PatrolTasks.AsNoTracking().SingleAsync(t=>t.Id!=first.TaskId));
        var secondAttempt=await f.Read(db=>db.InspectionAttempts.AsNoTracking().Where(a=>a.PatrolTaskItem.PatrolTaskId==secondTask.Id).OrderBy(a=>a.Id).FirstAsync());
        var a=await f.Login();var b=await f.Login("dev-ipqa-2");first=first with{A=a,B=a};var source=await WorkContextFixture.Issue(f,secondTask.Id,b);
        var second=first with{TaskId=secondTask.Id,ItemId=secondAttempt.PatrolTaskItemId,AttemptId=secondAttempt.Id,A=b,B=b,Source=source};
        var bytes=PhotoFixture.Image();var one=PhotoFixture.Input(first,bytes);var two=PhotoFixture.Input(second,bytes) with{ClientPhotoId=one.ClientPhotoId};
        var holder=0;var waiter=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync=(command,_)=> {
            if(command.Connection is NpgsqlConnection c && command.CommandText.Contains("INSERT INTO \"InspectionAttachments\"",StringComparison.Ordinal)) {
                if(Interlocked.CompareExchange(ref holder,c.ProcessID,0)!=0 && holder!=c.ProcessID) waiter.TrySetResult(c.ProcessID);
            }return Task.CompletedTask;
        };
        f.Fault.Arm("INSERT INTO \"InspectionAttachments\"");var winning=first.Client().PostAsJsonAsync(PhotoFixture.Root(first),one);Task<HttpResponseMessage>? losing=null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));losing=second.Client().PostAsJsonAsync(PhotoFixture.Root(second),two);
            if(f.PostgresRoot is not null) {
                var pid=await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10));await using var monitor=new NpgsqlConnection(f.Connection);await monitor.OpenAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while(true) {await using var q=new NpgsqlCommand("SELECT @h = ANY(pg_blocking_pids(@w))",monitor);q.Parameters.AddWithValue("h",holder);q.Parameters.AddWithValue("w",pid);if((bool)(await q.ExecuteScalarAsync(deadline.Token))!) break;await Task.Delay(20,deadline.Token);}
                Assert.False(losing.IsCompleted);
            }
        } finally {f.Fault.Release.TrySetResult();}
        Assert.Equal(HttpStatusCode.OK,(await winning).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await losing!).StatusCode);
        Assert.Equal(1,await f.Read(db=>db.InspectionAttachments.CountAsync()));Assert.Empty(Directory.GetFiles(p.DirectoryPath));
    }
}
