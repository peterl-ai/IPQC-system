using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ItemRemovalConcurrencyTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RM08_Sample_and_item_operations_share_one_CAS(bool removalFirst)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Cas(f, removalFirst); }
    internal static async Task Cas(PersonalSessionFixture f, bool removalFirst) {
        var s = await SharedDraftFixture.Setup(f); var removal = ItemRemovalTests.Input(s); var sample = s.Batch(s.Capture());
        f.Fault.Arm(removalFirst ? "INSERT INTO \"ItemRemovalOperations\"" : "INSERT INTO \"ContributionOperations\"");
        Task<HttpResponseMessage>? rm = removalFirst ? s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), removal) : null;
        Task<HttpResponseMessage>? capture = removalFirst ? null : s.Client().PostAsJsonAsync(s.Root + "/operation-batches", sample);
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            rm ??= s.Client(s.A).PostAsJsonAsync(ItemRemovalTests.Url(s), removal);
            capture ??= s.Client().PostAsJsonAsync(s.Root + "/operation-batches", sample);
        } finally { f.Fault.Release.TrySetResult(); }
        var responses = await Task.WhenAll(rm!, capture!);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await ItemRemovalTests.Counts(s, 1, removalFirst ? 1 : 0, removalFirst ? 0 : 1, removalFirst ? 0 : 1, 1);
    }

    [Theory][InlineData(false,"explicit")][InlineData(true,"explicit")][InlineData(false,"reset")][InlineData(true,"reset")]
    [InlineData(false,"role")][InlineData(true,"role")][InlineData(false,"deactivate")][InlineData(true,"deactivate")]
    public async Task RM07_Security_revocation_and_removal_both_orders(bool revokeFirst, string action)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Security(f, revokeFirst, action); }
    internal static async Task Security(PersonalSessionFixture f, bool revokeFirst, string action) {
        var s = await SharedDraftFixture.Setup(f); var b = ItemRemovalTests.Input(s);
        // Login before arming the account UPDATE barrier: otherwise Admin login's
        // own bookkeeping could masquerade as the revocation transaction.
        var admin = await f.Login("admin");
        async Task Revoke() {
            var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != "personal_session_id").ToList()));
            var response = action switch {
                "explicit" => await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{s.Source.Context.Id}/security-revoke", new WorkContextRevokeInput(1,"Synthetic removal race")),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic inspector","PQE")),
                _ => await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null)
            };
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        }
        var fragment = revokeFirst ? action == "explicit" ? "UPDATE \"WorkContexts\"" : "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"ItemRemovalOperations\"";
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (command, _) => {
            if (command.Connection is NpgsqlConnection connection && command.Transaction is not null) {
                if (command.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, connection.ProcessID, 0);
                else if (holder != 0 && connection.ProcessID != holder && command.CommandText.Contains("FOR ", StringComparison.Ordinal)) waiter.TrySetResult(connection.ProcessID);
            }
            return Task.CompletedTask;
        };
        f.Fault.Arm(fragment);
        var accepting = revokeFirst ? null : s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), b);
        var revoking = revokeFirst ? Revoke() : null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            accepting ??= s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), b); revoking ??= Revoke();
            if (f.PostgresRoot is not null) {
                var pid = await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using var monitor = new NpgsqlConnection(f.Connection); await monitor.OpenAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (true) {
                    await using var query = new NpgsqlCommand("SELECT @holder = ANY(pg_blocking_pids(@waiter))", monitor);
                    query.Parameters.AddWithValue("holder", holder); query.Parameters.AddWithValue("waiter", pid);
                    if ((bool)(await query.ExecuteScalarAsync(deadline.Token))!) break;
                    await Task.Delay(20, deadline.Token);
                }
                Assert.False(accepting.IsCompleted); Assert.False(revoking.IsCompleted);
            }
        } finally { f.Fault.Release.TrySetResult(); }
        var response = await accepting!; await revoking!;
        Assert.Equal(revokeFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        await ItemRemovalTests.Counts(s, revokeFirst ? 0 : 1, revokeFirst ? 0 : 1, 0, 0, revokeFirst ? 0 : 1);
        if (!revokeFirst) {
            var original = (await response.Content.ReadFromJsonAsync<ItemRemovalBatchResult>())!;
            Assert.Equal(original.Operation, (await ItemRemovalTests.Accept(s, b with { SourceProof = null })).Operation);
        }
    }

    [Theory][InlineData("logout")][InlineData("expiry")][InlineData("refresh")][InlineData("self-password")]
    public async Task RM06_Normal_A_changes_leave_saved_source_eligible(string action)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await NormalA(f, action); }
    internal static async Task NormalA(PersonalSessionFixture f, string action) {
        var s = await SharedDraftFixture.Setup(f); var b = ItemRemovalTests.Input(s);
        switch (action) {
            case "logout": Assert.Equal(HttpStatusCode.NoContent, (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).StatusCode); break;
            case "expiry": f.Clock.Now = f.Clock.Now.AddDays(15); break;
            case "refresh": Assert.Equal(s.A.PersonalSessionId, (await f.Refresh(s.A.RefreshToken!)).PersonalSessionId); break;
            default: (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password,"SyntheticChanged123A"))).EnsureSuccessStatusCode(); break;
        }
        s = s with { B = await f.Login("dev-ipqa-2") };
        var result = await ItemRemovalTests.Accept(s, b); Assert.Equal(s.A.User.Id, result.Operation.ContributorUserId);
        Assert.Null(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        await ItemRemovalTests.Counts(s, 1, 1, 0, 0, 1);
    }

    [Fact] public async Task RM10_Early_NotFound_late_commit_and_lost_response_recovery()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await LostResponse(f); }
    internal static async Task LostResponse(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = ItemRemovalTests.Input(s);
        f.Fault.Arm("FROM \"RefreshTokenSessions\""); var accepting = s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), b);
        HttpResponseMessage response;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.NotFound, (await s.Client(s.A).GetAsync(s.Root + $"/operation-batches/{b.BatchId}/receipt")).StatusCode);
        } finally { f.Fault.Release.TrySetResult(); response = await accepting; }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var original = (await response.Content.ReadFromJsonAsync<ItemRemovalBatchResult>())!;
        // Ignore the first response as a client would after losing its transport acknowledgement.
        var recovered = await s.Client(s.A).GetFromJsonAsync<DraftBatchReceipt>(s.Root + $"/operation-batches/{b.BatchId}/receipt");
        Assert.Equal(original.Operation, Assert.Single(recovered!.ItemOperations!));
        Assert.Equal(original.Operation, await s.Client().GetFromJsonAsync<ItemRemovalReceipt>(s.Root + $"/operations/{b.Operation.OperationId}/receipt"));
        Assert.Equal(original.Operation, (await ItemRemovalTests.Accept(s, b with { SourceProof = null }, s.A)).Operation);
        await ItemRemovalTests.Counts(s, 1, 1, 0, 0, 1);
    }

    [Theory][InlineData("INSERT INTO \"SharedDrafts\"")][InlineData("UPDATE \"SharedDrafts\"")]
    [InlineData("INSERT INTO \"AcceptedBatchReceipts\"")][InlineData("INSERT INTO \"ItemRemovalOperations\"")]
    [InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task RM14_Removal_fault_rolls_back_every_new_fact(string point)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Rollback(f, point, false); }
    [Theory][InlineData("INSERT INTO \"SharedAcceptanceAudits\"")][InlineData("commit")]
    public async Task RM14_Failed_removal_preserves_existing_sample_draft(string point)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Rollback(f, point, true); }
    internal static async Task Rollback(PersonalSessionFixture f, string point, bool populated) {
        var s = await SharedDraftFixture.Setup(f); var prior = populated ? 1 : 0;
        if (populated) await s.Accept(s.Batch(s.Capture()));
        var retained = await ItemRemovalTests.Retained(f); var b = ItemRemovalTests.Input(s, prior);
        if (point == "commit") f.CommitFault.Armed = true; else f.Fault.Arm(point, true);
        Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client().PostAsJsonAsync(ItemRemovalTests.Url(s), b)).StatusCode);
        if (point == "commit") Assert.False(f.CommitFault.Armed); else Assert.True(f.Fault.Entered.Task.IsCompletedSuccessfully);
        await ItemRemovalTests.Counts(s, prior, 0, prior, prior, prior); Assert.Equal(retained, await ItemRemovalTests.Retained(f));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(s.Root + $"/operations/{b.Operation.OperationId}/receipt")).StatusCode);
        await ItemRemovalTests.Accept(s, b); await ItemRemovalTests.Counts(s, prior + 1, 1, prior, prior, prior + 1);
    }
}
