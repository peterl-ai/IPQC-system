using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.WorkContextFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextSecurityTests
{
    [Theory][InlineData("logout")][InlineData("expiry")][InlineData("self-password")]
    public async Task R04_09_21_Normal_session_changes_preserve_historical_proof(string action)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login(); var issued = await Issue(f, task, a);
        if (action == "logout") await f.Client(a.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(a.RefreshToken));
        if (action == "self-password") await f.Client(a.AccessToken).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"));
        if (action == "expiry") f.Clock.Now = f.Clock.Now.AddDays(15);
        var b = await f.Login("dev-ipqa-2");
        Assert.Equal(HttpStatusCode.OK, (await f.Client(b.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued))).StatusCode);
        Assert.Null(await f.Read(db => db.WorkContexts.Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
    }
    [Theory][InlineData("reset")][InlineData("deactivate")][InlineData("role")][InlineData("explicit")]
    public async Task R04_10_11_18_19_Security_revocation_is_sticky_and_legacy_admin_does_not_need_family(string action)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Security(f, action); }
    internal static async Task Security(PersonalSessionFixture f, string action)
    {
        var task = await Task(f); var a = await f.Login(); var issued = await Issue(f, task, a); var admin = await f.Login("admin");
        var legacy = f.Client(f.Token(admin, claims => claims.Where(c => c.Type != "personal_session_id").ToList()));
        var url = $"/api/admin/work-contexts/{issued.Context.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await legacy.GetAsync(url)).StatusCode);
        var management = action switch {
            "reset" => await legacy.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
            "deactivate" => await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null),
            "role" => await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic inspector", "PQE")),
            _ => await f.Client(admin.AccessToken).PostAsJsonAsync(url + "/security-revoke", new WorkContextRevokeInput(1, "Synthetic security action")) };
        Assert.True(management.IsSuccessStatusCode, await management.Content.ReadAsStringAsync());
        if (action == "deactivate") await legacy.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/activate", null);
        if (action == "role") await legacy.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic inspector", "IPQA"));
        var b = await f.Login("dev-ipqa-2");
        var result = await f.Client(b.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.Contains("SourceSecurityRevoked", await result.Content.ReadAsStringAsync());
        Assert.Equal(2, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
        var audit = await f.Client(admin.AccessToken).GetAsync(url + "/audit?page=1&pageSize=1");
        var auditJson = System.Text.Json.JsonDocument.Parse(await audit.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, auditJson.GetProperty("total").GetInt32());
        Assert.Equal(1, auditJson.GetProperty("items").GetArrayLength());
        Assert.EndsWith("Z", auditJson.GetProperty("items")[0].GetProperty("occurredAtUtc").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Client(admin.AccessToken).PostAsJsonAsync(url + "/security-revoke", new WorkContextRevokeInput(1, "Retry"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(admin.AccessToken).PostAsJsonAsync(url + "/security-revoke", new WorkContextRevokeInput(2, "Retry"))).StatusCode);
        Assert.Equal(2, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => (await db.WorkContexts.SingleAsync()).CaptureUntilUtc = f.Clock.Now.UtcDateTime));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Edit(async db => db.WorkContextAuditEvents.Remove(await db.WorkContextAuditEvents.FirstAsync())));
    }
    [Theory][InlineData("reset")][InlineData("deactivate")][InlineData("role")]
    public async Task R04_26_Audit_failure_rolls_back_account_sessions_and_contexts(string action)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Rollback(f, action); }
    internal static async Task Rollback(PersonalSessionFixture f, string action)
    {
        var task = await Task(f); var a = await f.Login(); await Issue(f, task, a);
        f.Fault.Arm("INSERT INTO \"WorkContextAuditEvents\"", failure: true);
        using var scope = f.Factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<ApplicationUserService>();
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            if (action == "reset") await users.ResetPasswordAsync(TestAccounts.Ipqa1Id, "SyntheticChanged123A", TestAccounts.AdminId, "admin", default);
            else if (action == "role") await users.UpdateAsync(TestAccounts.Ipqa1Id, new UserUpdateInput("Synthetic inspector", "PQE"), TestAccounts.AdminId, "admin", default);
            else await users.SetActiveAsync(TestAccounts.Ipqa1Id, false, TestAccounts.AdminId, "admin", default);
        });
        Assert.Null(await f.Read(db => db.WorkContexts.Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        Assert.Equal(1, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
        Assert.Equal(HttpStatusCode.OK, (await f.Client(a.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.NotNull(await f.Login());
    }
}
