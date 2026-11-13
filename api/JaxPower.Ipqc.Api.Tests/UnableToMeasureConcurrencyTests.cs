using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class UnableToMeasureConcurrencyTests
{
    private static void Observe(PersonalSessionFixture f, string fragment, Action<int> owner, Func<int> held, TaskCompletionSource<int> waiter) {
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) owner(n.ProcessID);
                else if (held() != 0 && n.ProcessID != held() && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
    }
    private static async Task Blocked(PersonalSessionFixture f, int holder, Task<int> waiter) {
        if (f.PostgresRoot is null) return; var pid = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
        await using var monitor = new NpgsqlConnection(f.Connection); await monitor.OpenAsync(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) { await using var q = new NpgsqlCommand("SELECT @h=ANY(pg_blocking_pids(@w))", monitor); q.Parameters.AddWithValue("h", holder); q.Parameters.AddWithValue("w", pid);
            if ((bool)(await q.ExecuteScalarAsync(deadline.Token))!) return; await Task.Delay(20, deadline.Token); }
    }
    [Theory][InlineData(true, "explicit")][InlineData(false, "explicit")][InlineData(true, "reset")][InlineData(false, "reset")]
    [InlineData(true, "role")][InlineData(false, "role")][InlineData(true, "deactivate")][InlineData(false, "deactivate")]
    public async Task Source_security_both_orders(bool first, string action) { await using var p = await PhotoFixture.Create(); await Security(p, first, action); }
    internal static async Task Security(PhotoFixture p, bool revokeFirst, string action) {
        var f = p.F; var u = await UnableToMeasureTests.Setup(p); var s = u.S; var b = UnableToMeasureTests.Input(u); var admin = await f.Login("admin");
        async Task Revoke() {
            var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != "personal_session_id").ToList()));
            var r = action switch {
                "explicit" => await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke", new WorkContextRevokeInput(1, "Synthetic unable race")),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic inspector", "PQE")),
                _ => await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null) }; Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }
        var fragment = revokeFirst ? action == "explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"UnableToMeasureOperations\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Observe(f, fragment, pid => Interlocked.CompareExchange(ref holder, pid, 0), () => holder, waiter);
        f.Fault.Arm(fragment); Task<HttpResponseMessage>? accept = revokeFirst ? null : s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), b); Task? revoke = revokeFirst ? Revoke() : null;
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); accept ??= s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), b); revoke ??= Revoke();
            await Blocked(f, holder, waiter.Task); if (f.PostgresRoot is not null) { Assert.False(accept.IsCompleted); Assert.False(revoke.IsCompleted); } }
        finally { f.Fault.Release.TrySetResult(); }
        var result = await accept!; await revoke!; Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        await UnableToMeasureTests.Counts(u, revokeFirst ? 1 : 2, revokeFirst ? 0 : 1, !revokeFirst);
        if (!revokeFirst) {
            var r = (await UnableToMeasureTests.Accept(u, b with { SourceProof = null })).Operation; Assert.Equal(s.A.User.Id, r.ContributorUserId); Assert.Equal(s.B.User.Id, r.ActorUserId);
            Assert.Equal(r, await s.Client().GetFromJsonAsync<UnableToMeasureReceipt>(s.Root + $"/operations/{r.OperationId}/receipt"));
        }
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Current_actor_logout_order(bool first) { await using var p = await PhotoFixture.Create(); await Logout(p, first); }
    internal static async Task Logout(PhotoFixture p, bool first) {
        var f = p.F; var u = await UnableToMeasureTests.Setup(p); var s = u.S; var b = UnableToMeasureTests.Input(u);
        var fragment = first ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"UnableToMeasureOperations\""; var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Observe(f, fragment, pid => Interlocked.CompareExchange(ref holder, pid, 0), () => holder, waiter); f.Fault.Arm(fragment);
        Task<HttpResponseMessage>? accept = first ? null : s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), b);
        Task<HttpResponseMessage>? logout = first ? s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken)) : null;
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); accept ??= s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), b); logout ??= s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken)); await Blocked(f, holder, waiter.Task); }
        finally { f.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.NoContent, (await logout!).StatusCode); Assert.Equal(first ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, (await accept!).StatusCode);
        Assert.Equal(0, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.PersonalSessionId == s.B.PersonalSessionId && x.RevokedAtUtc == null)));
        u = u with { S = s with { B = await f.Login("dev-ipqa-2") } }; await UnableToMeasureTests.Counts(u, first ? 1 : 2, first ? 0 : 1, !first);
    }
    [Theory][InlineData("unable")][InlineData("sample")][InlineData("item")][InlineData("detach")]
    public async Task Four_domains_share_one_CAS(string first) { await using var p = await PhotoFixture.Create(); await Cas(p, first); }
    internal static async Task Cas(PhotoFixture p, string first) {
        var f = p.F; var u = await UnableToMeasureTests.Setup(p); var s = u.S;
        Task<HttpResponseMessage> Write(string kind) => kind switch {
            "unable" => s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), UnableToMeasureTests.Input(u)),
            "sample" => s.Client().PostAsJsonAsync(s.Root + "/operation-batches", s.Batch(s.Capture(version: 1), 1)),
            "item" => s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(s, 1)),
            _ => s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, u.Photo, 1, previous: u.Reference, kind: "DetachAttachment")) };
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Observe(f, "UPDATE \"SharedDrafts\"", pid => Interlocked.CompareExchange(ref holder, pid, 0), () => holder, waiter);
        f.Fault.Arm("UPDATE \"SharedDrafts\""); var winner = Write(first); var others = new List<Task<HttpResponseMessage>>();
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); foreach (var k in new[] { "unable", "sample", "item", "detach" }.Where(x => x != first)) others.Add(Write(k)); await Blocked(f, holder, waiter.Task); }
        finally { f.Fault.Release.TrySetResult(); }
        var results = await Task.WhenAll(others.Prepend(winner)); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, (await s.Read())!.DraftVersion); Assert.Equal(2, await f.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(2, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(first == "unable" ? 1 : 0, await f.Read(db => db.UnableToMeasureOperations.CountAsync()));
    }
    [Fact] public async Task Late_commit_and_lost_response_preserve_original_receipt() { await using var p = await PhotoFixture.Create(); await Lost(p); }
    internal static async Task Lost(PhotoFixture p) {
        var f = p.F; var u = await UnableToMeasureTests.Setup(p); var s = u.S; var b = UnableToMeasureTests.Input(u);
        f.Fault.Arm("FROM \"RefreshTokenSessions\""); var accepting = s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), b);
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(HttpStatusCode.NotFound, (await s.Client(s.A).GetAsync(s.Root + $"/operations/{b.Operation.OperationId}/receipt")).StatusCode); }
        finally { f.Fault.Release.TrySetResult(); }
        var response = await accepting; Assert.Equal(HttpStatusCode.OK, response.StatusCode); var original = (await response.Content.ReadFromJsonAsync<UnableToMeasureBatchResult>())!.Operation;
        Assert.Equal(original, (await UnableToMeasureTests.Accept(u, b with { SourceProof = null }, s.A)).Operation);
        var clear = UnableToMeasureTests.Input(u, 2, kind: "ClearUnableToMeasureNG", previous: original);
        f.Fault.ObserveAsync = (c, _) => { if (c.CommandText.Contains("INSERT INTO \"UnableToMeasureOperations\"", StringComparison.Ordinal)) f.CommitFault.AfterCommit = () => throw new InvalidOperationException("Synthetic response lost after commit"); return Task.CompletedTask; };
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(UnableToMeasureTests.Url(s), clear)).StatusCode); f.Fault.ObserveAsync = null;
        Assert.True((await UnableToMeasureTests.Accept(u, clear with { SourceProof = null })).ReplayOnly); await UnableToMeasureTests.Counts(u, 3, 2, false);
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Physical_loss_before_first_acceptance_is_rechecked(bool corrupt) { await using var p = await PhotoFixture.Create(); await PhysicalLoss(p, corrupt); }
    internal static async Task PhysicalLoss(PhotoFixture p, bool corrupt) {
        var u = await UnableToMeasureTests.Setup(p); p.F.Fault.Arm("FROM \"RefreshTokenSessions\""); var accepting = u.S.Client().PostAsJsonAsync(UnableToMeasureTests.Url(u.S), UnableToMeasureTests.Input(u));
        try { await p.F.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); if (corrupt) await File.WriteAllBytesAsync(await p.FinalPath(u.Photo.AttachmentId), [1]); else File.Delete(await p.FinalPath(u.Photo.AttachmentId)); }
        finally { p.F.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await accepting).StatusCode); await UnableToMeasureTests.Counts(u, 1, 0, false);
    }
}
