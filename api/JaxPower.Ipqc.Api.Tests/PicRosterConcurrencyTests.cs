using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.PicRosterTests;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PicRosterConcurrencyTests
{
    internal static async Task<AuthOutput> OtherAdmin(PersonalSessionFixture f, HttpClient admin) {
        var created = await admin.PostAsJsonAsync("/api/users", new UserCreateInput("synthetic-admin-two", "Synthetic second Admin", "Admin", TestAccounts.Password, true)); created.EnsureSuccessStatusCode();
        var initial = await f.Login("synthetic-admin-two");
        const string password = "SyntheticChanged123A";
        (await f.Client(initial.AccessToken).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, password))).EnsureSuccessStatusCode();
        return await f.Login("synthetic-admin-two", password);
    }
    private static async Task Race(PersonalSessionFixture f, string fragment, Func<Task> first, Func<Task> second, bool identityWait = true) {
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (command, _) => {
            if (command.Connection is NpgsqlConnection pg && command.Transaction is not null) {
                if (command.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, pg.ProcessID, 0);
                else if (holder != 0 && holder != pg.ProcessID &&
                    command.CommandText.Contains(identityWait ? "PicRosterEntries" : "ApplicationUsers", StringComparison.Ordinal) &&
                    command.CommandText.Contains(identityWait ? "FOR UPDATE" : "FOR ", StringComparison.Ordinal)) waiter.TrySetResult(pg.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); var one = first(); Task? two = null;
        try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); two = second();
            if (f.PostgresRoot is not null) { await PostgresBlocking.Wait(f.Connection, holder, await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10))); Assert.False(one.IsCompleted); Assert.False(two.IsCompleted); }
        } finally { f.Fault.Release.TrySetResult(); }
        await one; await two!; f.Fault.ObserveAsync = null;
    }
    [Theory][InlineData(true, "edit")][InlineData(false, "edit")][InlineData(true, "deactivate")][InlineData(false, "deactivate")][InlineData(true, "retry")][InlineData(false, "retry")]
    public async Task Two_actual_Admins_cannot_silently_overwrite_one_revision(bool first, string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Cas(f, first, kind); }
    internal static async Task Cas(PersonalSessionFixture f, bool aFirst, string kind) {
        var a = await f.Login("admin"); var ac = f.Client(a.AccessToken); var b = await OtherAdmin(f, ac); var bc = f.Client(b.AccessToken);
        var initial = (await Accepted(await ac.PostAsJsonAsync(Root, Input()))).Receipt;
        var edit = new PicEditInput(Guid.NewGuid(), 1, "A edit", "A department"); var other = kind == "retry" ? edit : new(Guid.NewGuid(), 1, "B edit", "B department");
        HttpResponseMessage? ar = null, br = null;
        async Task A() { ar = await ac.PutAsJsonAsync(Root + "/" + initial.PicId, edit); }
        async Task B() { br = kind == "deactivate" ? await bc.PostAsJsonAsync(Root + $"/{initial.PicId}/deactivate", new PicRevisionInput(Guid.NewGuid(), 1)) : await bc.PutAsJsonAsync(Root + "/" + initial.PicId, other); }
        await Race(f, "INSERT INTO \"PicRosterRevisions\"", aFirst ? A : B, aFirst ? B : A);
        Assert.Equal(aFirst || kind == "retry" ? HttpStatusCode.OK : HttpStatusCode.Conflict, ar!.StatusCode); Assert.Equal(!aFirst || kind == "retry" ? HttpStatusCode.OK : HttpStatusCode.Conflict, br!.StatusCode);
        if (kind == "retry") {
            var winner = await Accepted(aFirst ? ar : br); var replay = await Accepted(aFirst ? br : ar);
            Assert.False(winner.ReplayOnly); Assert.True(replay.ReplayOnly); Assert.Equal(winner.Receipt, replay.Receipt);
        } else Assert.Contains("PicRevisionConflict", await (aFirst ? br : ar).Content.ReadAsStringAsync());
        var current = (await ac.GetFromJsonAsync<PicOutput>(Root + "/" + initial.PicId))!; Assert.Equal(2, current.Revision);
        Assert.Equal(aFirst || kind == "retry" ? "A edit" : kind == "edit" ? "B edit" : initial.Name, current.Name);
        Assert.Equal(aFirst || kind != "deactivate", current.IsActive); Assert.Equal(2, await f.Read(db => db.PicRosterRevisions.CountAsync()));
        var history = (await ac.GetFromJsonAsync<PicPage<PicRevisionReceipt>>(Root + $"/{initial.PicId}/history"))!;
        Assert.Equal(aFirst ? a.User.Id : b.User.Id, history.Items[0].ActorUserId); Assert.Equal(initial, history.Items[1]);
        if (kind == "retry") { var before = await Facts(f); var retry = await Accepted(await (aFirst ? bc : ac).PutAsJsonAsync(Root + "/" + initial.PicId, edit)); Assert.True(retry.ReplayOnly); Assert.Equal(history.Items[0], retry.Receipt); Assert.Equal(before, await Facts(f)); }
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Admin_logout_and_edit_have_a_real_authority_frontier(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await Logout(f, first); }
    internal static async Task Logout(PersonalSessionFixture f, bool logoutFirst) {
        var a = await f.Login("admin"); var ac = f.Client(a.AccessToken); var other = await OtherAdmin(f, ac); var bc = f.Client(other.AccessToken);
        var initial = (await Accepted(await ac.PostAsJsonAsync(Root, Input()))).Receipt; HttpResponseMessage? changed = null;
        async Task Edit() { changed = await ac.PutAsJsonAsync(Root + "/" + initial.PicId, new PicEditInput(Guid.NewGuid(), 1, "Changed", "Department")); }
        async Task Close() { Assert.Equal(HttpStatusCode.NoContent, (await ac.PostAsJsonAsync("/api/auth/logout", new RefreshInput(a.RefreshToken))).StatusCode); }
        await Race(f, logoutFirst ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"PicRosterRevisions\"", logoutFirst ? Close : Edit, logoutFirst ? Edit : Close, false);
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, changed!.StatusCode);
        Assert.Equal(logoutFirst ? 1 : 2, (await bc.GetFromJsonAsync<PicOutput>(Root + "/" + initial.PicId))!.Revision);
        Assert.Equal(initial, await bc.GetFromJsonAsync<PicRevisionReceipt>(Root + $"/requests/{initial.RequestId}/receipt"));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Concurrent_create_request_uniqueness_leaves_no_orphan_identity(bool changed) { await using var f = await PersonalSessionFixture.CreateAsync(); await CreateRace(f, changed); }
    internal static async Task CreateRace(PersonalSessionFixture f, bool changed) {
        var a = await f.Login("admin"); var ac = f.Client(a.AccessToken); var b = await OtherAdmin(f, ac); var bc = f.Client(b.AccessToken);
        var input = Input(); var other = changed ? input with { Name = "B distinct intent" } : input;
        HttpResponseMessage ar, br;
        if (f.PostgresRoot is not null) {
            var holder = 0; var otherPid = 0;
            f.Fault.ObserveAsync = (command, _) => { if (command.Connection is NpgsqlConnection pg && command.CommandText.Contains("INSERT INTO \"PicRosterRevisions\"", StringComparison.Ordinal)) {
                if (Interlocked.CompareExchange(ref holder, pg.ProcessID, 0) != 0) otherPid = pg.ProcessID;
            } return Task.CompletedTask; };
            f.Fault.Arm("INSERT INTO \"PicRosterRevisions\""); var pending = ac.PostAsJsonAsync(Root, input);
            try { await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); br = await bc.PostAsJsonAsync(Root, other);
                Assert.Equal(HttpStatusCode.Created, br.StatusCode); Assert.False(pending.IsCompleted); Assert.NotEqual(0, otherPid); Assert.NotEqual(holder, otherPid);
            } finally { f.Fault.Release.TrySetResult(); }
            ar = await pending; f.Fault.ObserveAsync = null; Assert.Equal(HttpStatusCode.Conflict, ar.StatusCode);
        } else { var results = await Task.WhenAll(ac.PostAsJsonAsync(Root, input), bc.PostAsJsonAsync(Root, other)); ar = results[0]; br = results[1]; }
        Assert.Single(new[] { ar, br }, x => x.StatusCode == HttpStatusCode.Created);
        var winner = (await Accepted(ar.StatusCode == HttpStatusCode.Created ? ar : br)).Receipt;
        Assert.Equal(1, await f.Read(db => db.PicRosterEntries.CountAsync())); Assert.Equal(1, await f.Read(db => db.PicRosterRevisions.CountAsync()));
        var before = await Facts(f); var retry = await ac.PostAsJsonAsync(Root, input);
        if (changed && winner.Name != input.Name) Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        else { var replay = await Accepted(retry); Assert.True(replay.ReplayOnly); Assert.Equal(winner, replay.Receipt); }
        Assert.Equal(before, await Facts(f));
    }
}
