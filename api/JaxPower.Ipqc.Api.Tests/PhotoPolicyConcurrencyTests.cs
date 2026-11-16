using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoPolicyConcurrencyTests
{
    private static async Task Race(PersonalSessionFixture f, string fragment, Func<Task> first, Func<Task> second) {
        var holder = 0; var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Fault.ObserveAsync = (c, _) => {
            if (c.Connection is NpgsqlConnection n && c.Transaction is not null) {
                if (c.CommandText.Contains(fragment, StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, n.ProcessID, 0);
                else if (holder != 0 && n.ProcessID != holder && (c.CommandText.Contains("FOR ", StringComparison.Ordinal) || c.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))) waiter.TrySetResult(n.ProcessID);
            } return Task.CompletedTask;
        };
        f.Fault.Arm(fragment); var one = first(); Task? two = null;
        try {
            await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); two = second();
            if (f.PostgresRoot is not null) await PostgresBlocking.Wait(f.Connection, holder, await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        } finally { f.Fault.Release.TrySetResult(); }
        await one; await two!;
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Confirmation_and_real_Standard_edit_serialize(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await Definition(f, first); }
    internal static async Task Definition(PersonalSessionFixture f, bool confirmFirst) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe"); var admin = await f.Login("admin");
        var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var input = PhotoPolicyFixture.Input(item, "Required");
        var definition = PhotoPolicyTests.Edit((await f.Client(admin.AccessToken).GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{s}"))!);
        definition = definition with { InspectionItems = definition.InspectionItems!.Select((x, i) => i == 0 ? x with { InspectionContent = "Concurrent changed definition" } : x).ToList() };
        HttpResponseMessage? accepted = null;
        async Task Confirm() { accepted = await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId), input); }
        async Task Edit() { (await f.Client(admin.AccessToken).PutAsJsonAsync($"/api/patrol-standards/{s}", definition)).EnsureSuccessStatusCode(); }
        await Race(f, confirmFirst ? "INSERT INTO \"PhotoPolicyConfirmations\"" : "UPDATE \"PatrolStandardItems\"", confirmFirst ? Confirm : Edit, confirmFirst ? Edit : Confirm);
        Assert.Equal(confirmFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict, accepted!.StatusCode);
        Assert.Null((await PhotoPolicyFixture.Read(f, s, pqe)).Items[0].Confirmation);
        Assert.Equal(confirmFirst ? 1 : 0, await f.Read(db => db.PhotoPolicyConfirmations.CountAsync()));
        if (confirmFirst) Assert.Equal(2, await f.Read(db => db.PhotoPolicyDefinitions.CountAsync()));
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Confirmation_and_scheduler_freeze_one_consistent_revision(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await Generation(f, first); }
    internal static async Task Generation(PersonalSessionFixture f, bool confirmFirst) {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero); var s = await LineSharedPlanningFixture.Standard(f); await LineSharedPlanningFixture.Plan(f, s);
        var pqe = await f.Login("pqe"); var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var input = PhotoPolicyFixture.Input(item, "Required"); f.Clock.Now = f.Clock.Now.AddMinutes(1);
        PhotoPolicyReceipt? receipt = null; int generated = 0;
        async Task Confirm() { receipt = await PhotoPolicyFixture.Confirm(f, s, item.StandardItemId, input, pqe); }
        async Task Generate() { generated = await PhotoPolicyFixture.GenerateStrict(f); }
        await Race(f, confirmFirst ? "INSERT INTO \"PhotoPolicyConfirmations\"" : "INSERT INTO \"FrozenTaskPhotoPolicies\"", confirmFirst ? Confirm : Generate, confirmFirst ? Generate : Confirm);
        Assert.Equal(1, generated); var snapshot = await f.Read(db => db.FrozenTaskPhotoPolicies.Join(db.PatrolTaskItems, p => p.TaskItemId, i => i.Id, (p, i) => new { p, i.SourcePatrolStandardItemId })
            .Where(x => x.SourcePatrolStandardItemId == item.StandardItemId).Select(x => x.p.SnapshotJson).SingleAsync());
        var frozen = System.Text.Json.JsonSerializer.Deserialize<FrozenPhotoPolicy>(snapshot, JaxPower.Ipqc.Api.Services.DraftCanonical.Json)!;
        Assert.Equal(confirmFirst ? receipt : item.Confirmation, frozen.Policy); Assert.Equal(confirmFirst ? "Required" : "Optional", frozen.Policy.Value);
        Assert.Equal(2, await f.Read(db => db.FrozenTaskPhotoPolicies.CountAsync())); Assert.Equal(1, await f.Read(db => db.PatrolTasks.CountAsync()));
    }
    [Theory][InlineData(true)][InlineData(false)] public async Task Confirmation_and_Plan_revision_have_coherent_source_contracts(bool first) { await using var f = await PersonalSessionFixture.CreateAsync(); await Plan(f, first); }
    internal static async Task Plan(PersonalSessionFixture f, bool confirmFirst) {
        f.Clock.Now = new(2026, 11, 11, 11, 59, 0, TimeSpan.Zero); var s = await LineSharedPlanningFixture.Standard(f); var plan = await LineSharedPlanningFixture.Plan(f, s);
        var pqe = await f.Login("pqe"); var admin = await f.Login("admin"); var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0];
        async Task Confirm() { await PhotoPolicyFixture.Confirm(f, s, item.StandardItemId, PhotoPolicyFixture.Input(item, "Required"), pqe); }
        async Task Edit() { (await f.Client(admin.AccessToken).PutAsJsonAsync($"/api/line-patrol-plans/{plan.Id}", LineSharedPlanningFixture.Edit(plan) with { PlanName = "Concurrent Plan revision" })).EnsureSuccessStatusCode(); }
        await Race(f, confirmFirst ? "INSERT INTO \"PhotoPolicyConfirmations\"" : "UPDATE \"PatrolPlans\"", confirmFirst ? Confirm : Edit, confirmFirst ? Edit : Confirm);
        f.Clock.Now = f.Clock.Now.AddMinutes(1); Assert.Equal(1, await PhotoPolicyFixture.GenerateStrict(f));
        var frozen = await f.Read(db => db.FrozenTaskPhotoPolicies.Select(x => x.SnapshotJson).ToListAsync());
        Assert.All(frozen, json => Assert.Equal(2, System.Text.Json.JsonSerializer.Deserialize<FrozenPhotoPolicy>(json, JaxPower.Ipqc.Api.Services.DraftCanonical.Json)!.SourcePlanRevision));
    }
    [Theory][InlineData(true, "reset")][InlineData(false, "reset")][InlineData(true, "role")][InlineData(false, "role")]
    [InlineData(true, "deactivate")][InlineData(false, "deactivate")][InlineData(true, "logout")][InlineData(false, "logout")]
    public async Task Current_signer_security_closure_has_a_real_acceptance_frontier(bool first, string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await Security(f, first, action); }
    internal static async Task Security(PersonalSessionFixture f, bool confirmFirst, string action) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe"); var admin = await f.Login("admin");
        var item = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var input = PhotoPolicyFixture.Input(item); HttpResponseMessage? result = null;
        async Task Confirm() { result = await f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, item.StandardItemId), input); }
        async Task Revoke() {
            var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != "personal_session_id").ToList()));
            var r = action switch {
                "logout" => await f.Client(pqe.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(pqe.RefreshToken)),
                "reset" => await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.PqeId}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
                "role" => await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.PqeId}", new UserUpdateInput("Synthetic quality engineer", "IPQA")),
                _ => await legacy.PostAsync($"/api/users/{TestAccounts.PqeId}/deactivate", null) };
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }
        var fragment = confirmFirst ? "INSERT INTO \"PhotoPolicyConfirmations\"" : action == "logout" ? "UPDATE \"RefreshTokenSessions\"" : "UPDATE \"ApplicationUsers\"";
        await Race(f, fragment, confirmFirst ? Confirm : Revoke, confirmFirst ? Revoke : Confirm);
        if (confirmFirst) Assert.Equal(HttpStatusCode.OK, result!.StatusCode); else Assert.Contains(result!.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(confirmFirst ? 1 : 0, await f.Read(db => db.PhotoPolicyConfirmations.CountAsync()));
        Assert.Equal(confirmFirst ? 1 : 0, await f.Read(db => db.PhotoPolicyDefinitions.CountAsync()));
    }
    [Fact] public async Task Duplicate_and_competing_confirmation_are_idempotent_or_conflict() { await using var f = await PersonalSessionFixture.CreateAsync(); await Duplicate(f); }
    internal static async Task Duplicate(PersonalSessionFixture f) {
        var s = await LineSharedPlanningFixture.Standard(f, confirmPolicy: false); var pqe = await f.Login("pqe"); var i = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var input = PhotoPolicyFixture.Input(i);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, i.StandardItemId), input)));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode)); Assert.Equal(1, await f.Read(db => db.PhotoPolicyConfirmations.CountAsync()));
        i = (await PhotoPolicyFixture.Read(f, s, pqe)).Items[0]; var a = PhotoPolicyFixture.Input(i, "Required"); var b = PhotoPolicyFixture.Input(i, "Optional");
        results = await Task.WhenAll(new[] { f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, i.StandardItemId), a), f.Client(pqe.AccessToken).PostAsJsonAsync(PhotoPolicyFixture.ConfirmUrl(s, i.StandardItemId), b) });
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, await f.Read(db => db.PhotoPolicyConfirmations.CountAsync()));
    }
}
