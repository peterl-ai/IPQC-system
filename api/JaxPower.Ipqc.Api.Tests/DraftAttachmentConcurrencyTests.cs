using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DraftAttachmentConcurrencyTests
{
    private static async Task Blocked(PersonalSessionFixture f, int holder, Task<int> waiter) {
        if (f.PostgresRoot is null) return;
        var pid = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
        await PostgresBlocking.Wait(f.Connection, holder, pid);
    }
    [Theory][InlineData(true, "explicit")][InlineData(false, "explicit")][InlineData(true, "reset")][InlineData(false, "reset")]
    [InlineData(true, "role")][InlineData(false, "role")][InlineData(true, "deactivate")][InlineData(false, "deactivate")]
    public async Task Photo_source_security_both_serialization_orders(bool first, string action) { await using var p = await PhotoFixture.Create(); await Security(p, first, action); }
    internal static async Task Security(PhotoFixture p, bool revokeFirst, string action) {
        var f = p.F; var (s, a, _) = await DraftAttachmentTests.Setup(p); var sourceB = await WorkContextFixture.Issue(f, s.TaskId, s.B);
        var b = DraftAttachmentTests.Input(s, a, source: sourceB); var admin = await f.Login("admin"); // exclude login bookkeeping from the barrier.
        async Task Revoke() {
            var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != "personal_session_id").ToList()));
            var r = action switch {
                "explicit" => await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke", new WorkContextRevokeInput(1, "Synthetic reference race")),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic inspector", "PQE")),
                _ => await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null) };
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }
        var fragment = revokeFirst ? action == "explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"DraftAttachmentOperations\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); Task<HttpResponseMessage>? accept = revokeFirst ? null : s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b);
        Task? revoke = revokeFirst ? Revoke() : null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); accept ??= s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b); revoke ??= Revoke();
            await Blocked(f, holder, waiter.Task); if (f.PostgresRoot is not null) { Assert.False(accept.IsCompleted); Assert.False(revoke.IsCompleted); }
        } finally { f.Fault.Release.TrySetResult(); }
        var result = await accept!; await revoke!; Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        await DraftAttachmentTests.Counts(s, revokeFirst ? 0 : 1, revokeFirst ? 0 : 1, revokeFirst ? 0 : 1, revokeFirst ? 0 : 1);
        Assert.Equal(1, await f.Read(db => db.AttachmentAcceptances.CountAsync()));
        if (!revokeFirst) {
            var receipt = Assert.Single((await DraftAttachmentTests.Accept(s, b with { SourceProofs = [] })).Operations);
            Assert.Equal(s.A.User.Id, receipt.PhotoContributorUserId); Assert.Equal(s.B.User.Id, receipt.ActorUserId);
            Assert.Equal(receipt, await s.Client().GetFromJsonAsync<DraftAttachmentReceipt>(s.Root + $"/operations/{receipt.OperationId}/receipt"));
        }
    }
    [Theory][InlineData("photo")][InlineData("sample")][InlineData("item")]
    public async Task All_three_domains_share_one_CAS(string first) { await using var p = await PhotoFixture.Create(); await Cas(p, first); }
    internal static async Task Cas(PhotoFixture p, string first) {
        var f = p.F; var (s, a, _) = await DraftAttachmentTests.Setup(p);
        Task<HttpResponseMessage> Write(string kind) => kind switch {
            "photo" => s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), DraftAttachmentTests.Input(s, a)),
            "sample" => s.Client().PostAsJsonAsync(s.Root + "/operation-batches", s.Batch(s.Capture())),
            _ => s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), ItemRemovalTests.Input(s)) };
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n) {
                if (c.CommandText.Contains("UPDATE \"SharedDrafts\"", StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm("UPDATE \"SharedDrafts\""); var winning = Write(first); var others = new List<Task<HttpResponseMessage>>();
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); foreach (var k in new[] { "photo", "sample", "item" }.Where(x => x != first)) others.Add(Write(k));
            await Blocked(f, holder, waiter.Task);
        } finally { f.Fault.Release.TrySetResult(); }
        var results = await Task.WhenAll(others.Prepend(winning)); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Equal(2, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await s.Read())!.DraftVersion); Assert.Equal(1, await f.Read(db => db.DraftOperationKeys.CountAsync()));
        Assert.Equal(1, await f.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(1, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
    }
    [Fact] public async Task Lost_response_and_early_NotFound_are_reconciled() { await using var p = await PhotoFixture.Create(); await Lost(p); }
    internal static async Task Lost(PhotoFixture p) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); var b = DraftAttachmentTests.Input(s, a);
        p.F.Fault.Arm("FROM \"RefreshTokenSessions\""); var accepting = s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b);
        try { await p.F.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(HttpStatusCode.NotFound, (await s.Client(s.A).GetAsync(s.Root + $"/operations/{b.Operations[0].OperationId}/receipt")).StatusCode); }
        finally { p.F.Fault.Release.TrySetResult(); }
        var response = await accepting; Assert.Equal(HttpStatusCode.OK, response.StatusCode); var original = (await response.Content.ReadFromJsonAsync<DraftAttachmentBatchResult>())!;
        var replay = await DraftAttachmentTests.Accept(s, b with { SourceProofs = [] }, s.A); Assert.True(replay.ReplayOnly); Assert.Equal(Assert.Single(original.Operations), Assert.Single(replay.Operations));
        await DraftAttachmentTests.Counts(s, 1, 1, 1, 1);
        // A post-commit response failure still leaves a durable receipt, with no retry/reapplication.
        var detach = DraftAttachmentTests.Input(s, a, 1, previous: original.Operations[0], kind: "DetachAttachment");
        // Arm after current-family middleware, at the actual acceptance SQL, not an earlier transaction.
        p.F.Fault.ObserveAsync = (c, _) => { if (c.CommandText.Contains("INSERT INTO \"DraftAttachmentOperations\"", StringComparison.Ordinal)) p.F.CommitFault.AfterCommit = () => throw new InvalidOperationException("Synthetic response lost after commit"); return Task.CompletedTask; };
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), detach)).StatusCode); p.F.Fault.ObserveAsync = null;
        Assert.True((await DraftAttachmentTests.Accept(s, detach with { SourceProofs = [] })).ReplayOnly); await DraftAttachmentTests.Counts(s, 2, 2, 0, 2);
    }
    [Fact] public async Task Input_snapshot_matches_source_owner_discovery() { await using var p = await PhotoFixture.Create(); await Snapshot(p); }
    internal static async Task Snapshot(PhotoFixture p) {
        var (s, a, _) = await DraftAttachmentTests.Setup(p); var b = DraftAttachmentTests.Input(s, a); var original = b.Operations[0];
        await using var scope = p.F.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
        p.F.Fault.Arm("FROM \"InspectionAttachments\""); var pending = service.AcceptAttachmentsAsync(s.TaskId, b, WorkContextFixture.Principal(s.B), default);
        try { await p.F.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); b.Operations.Clear(); b.SourceProofs.Clear(); }
        finally { p.F.Fault.Release.TrySetResult(); }
        Assert.Equal(original.OperationId, Assert.Single((await pending).Operations).OperationId); await DraftAttachmentTests.Counts(s, 1, 1, 1, 1);
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Current_actor_logout_order(bool first) { await using var p = await PhotoFixture.Create(); await Logout(p, first); }
    internal static async Task Logout(PhotoFixture p, bool logoutFirst) {
        var f = p.F; var (s, a, _) = await DraftAttachmentTests.Setup(p); var b = DraftAttachmentTests.Input(s, a);
        var fragment = logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"DraftAttachmentOperations\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); Task<HttpResponseMessage>? accepting = logoutFirst ? null : s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b);
        Task<HttpResponseMessage>? logout = logoutFirst ? s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken)) : null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); accepting ??= s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b);
            logout ??= s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken)); await Blocked(f, holder, waiter.Task);
        } finally { f.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.NoContent, (await logout!).StatusCode); Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, (await accepting!).StatusCode);
        Assert.Equal(0, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.PersonalSessionId == s.B.PersonalSessionId && x.RevokedAtUtc == null)));
        s = s with { B = await f.Login("dev-ipqa-2") }; await DraftAttachmentTests.Counts(s, logoutFirst ? 0 : 1, logoutFirst ? 0 : 1, logoutFirst ? 0 : 1, logoutFirst ? 0 : 1);
    }
    [Fact] public async Task Real_Ready_commit_and_reference_writer_have_compatible_locks() { await using var p = await PhotoFixture.Create(); await ReadyRace(p); }
    internal static async Task ReadyRace(PhotoFixture p) {
        var f = p.F; var (s, a, bytes) = await DraftAttachmentTests.Setup(p, false); var b = DraftAttachmentTests.Input(s, a);
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains("INSERT INTO \"AttachmentAcceptances\"", StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && c.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm("INSERT INTO \"AttachmentAcceptances\""); var upload = PhotoFixture.Upload(s, a.AttachmentId, bytes); Task<HttpResponseMessage>? reference = null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.True(File.Exists(await p.FinalPath(a.AttachmentId)));
            reference = s.Client().PostAsJsonAsync(DraftAttachmentTests.Url(s), b); await Blocked(f, holder, waiter.Task);
        } finally { f.Fault.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.OK, (await upload).StatusCode); Assert.Equal(HttpStatusCode.OK, (await reference!).StatusCode); await DraftAttachmentTests.Counts(s, 1, 1, 1, 1);
    }
}
