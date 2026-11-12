using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.WorkContextFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextConcurrencyTests
{
    [Theory][InlineData(false, "reset")][InlineData(true, "reset")][InlineData(false, "role")][InlineData(true, "role")][InlineData(false, "deactivate")][InlineData(true, "deactivate")]
    public async Task R04_22_Issue_and_security_writer_serialize(bool securityFirst, string action)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await IssueRace(f, securityFirst, action); }
    internal static async Task IssueRace(PersonalSessionFixture f, bool securityFirst, string action = "reset")
    {
        var task = await Task(f); var auth = await f.Login();
        using var scope = f.Factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<ApplicationUserService>();
        System.Threading.Tasks.Task<UserOutput?> SecurityAction() => action switch {
            "role" => users.UpdateAsync(TestAccounts.Ipqa1Id, new UserUpdateInput("Synthetic inspector", "PQE"), TestAccounts.AdminId, "admin", default),
            "deactivate" => users.SetActiveAsync(TestAccounts.Ipqa1Id, false, TestAccounts.AdminId, "admin", default),
            _ => users.ResetPasswordAsync(TestAccounts.Ipqa1Id, "SyntheticChanged123A", TestAccounts.AdminId, "admin", default) };
        f.Fault.Arm(securityFirst ? "UPDATE \"ApplicationUsers\"" : "INSERT INTO \"WorkContexts\"");
        var issue = securityFirst ? null : f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f));
        var reset = securityFirst ? SecurityAction() : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        issue ??= f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f));
        reset ??= SecurityAction();
        f.Fault.Release.TrySetResult();
        var r = await issue; await reset;
        Assert.Equal(securityFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(0, await f.Read(db => db.WorkContexts.CountAsync(x => x.SecurityRevokedAtUtc == null)));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task R04_23_Preflight_and_explicit_revocation_serialize(bool revokeFirst)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await PreflightRace(f, revokeFirst); }
    internal static async Task PreflightRace(PersonalSessionFixture f, bool revokeFirst)
    {
        var task = await Task(f); var a = await f.Login(); var issued = await Issue(f, task, a); var b = await f.Login("dev-ipqa-2"); var admin = await f.Login("admin");
        f.Fault.Arm(revokeFirst ? "UPDATE \"WorkContexts\"" : "FROM \"PatrolTasks\" AS");
        var client = f.Client(b.AccessToken); var admins = f.Client(admin.AccessToken);
        var url = $"/api/admin/work-contexts/{issued.Context.Id}/security-revoke";
        // Direct service bypasses HTTP's earlier authority SELECT so the barrier is inside the transaction.
        using var scope = f.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WorkContextService>();
        var preflight = revokeFirst ? null : service.PreflightAsync(task, Sources(f, issued), Principal(b), default);
        var revoke = revokeFirst ? admins.PostAsJsonAsync(url, new WorkContextRevokeInput(1, "Synthetic revoke")) : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (revokeFirst) preflight = service.PreflightAsync(task, Sources(f, issued), Principal(b), default);
        revoke ??= admins.PostAsJsonAsync(url, new WorkContextRevokeInput(1, "Synthetic revoke"));
        f.Fault.Release.TrySetResult();
        if (revokeFirst) await Assert.ThrowsAsync<WorkContextSourceRevokedException>(() => preflight!); else Assert.False((await preflight!).AcceptsWork);
        Assert.True((await revoke).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued))).StatusCode);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task R04_27_Issuance_failure_never_delivers_proof_or_half_graph(bool precommit)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await IssueRollback(f, precommit); }
    internal static async Task IssueRollback(PersonalSessionFixture f, bool precommit)
    {
        var task = await Task(f); var auth = await f.Login();
        if (precommit) f.CommitFault.Armed = true; else f.Fault.Arm("INSERT INTO \"WorkContextAuditEvents\"", failure: true);
        using var scope = f.Factory.Services.CreateScope();
        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<WorkContextService>().IssueAsync(task, Input(f), Principal(auth), default));
        Assert.Equal(0, await f.Read(db => db.WorkContexts.CountAsync())); Assert.Equal(0, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
    }

    [Theory][InlineData(1)][InlineData(3)]
    public async Task R04_28_Known_rollback_retries_and_exhaustion_are_bounded(int failures)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var auth = await f.Login();
        f.Fault.ArmBusy(failures, "INSERT INTO \"WorkContexts\"");
        var r = await f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f));
        Assert.Equal(failures == 1 ? HttpStatusCode.Created : HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(failures == 1 ? 1 : 0, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task R04_24_Refresh_and_logout_order_preserve_current_uploader_authority(bool logout)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await FamilyRace(f, logout); }
    internal static async Task FamilyRace(PersonalSessionFixture f, bool logout)
    {
        var task = await Task(f); var a = await f.Login(); var issued = await Issue(f, task, a); var b = await f.Login("dev-ipqa-2");
        f.Fault.Arm(logout ? "UPDATE \"RefreshTokenSessions\"" : "INSERT INTO \"RefreshTokenSessions\"");
        var change = logout ? f.Client(b.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(b.RefreshToken)) :
            f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(b.RefreshToken));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var check = f.Client(b.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued));
        f.Fault.Release.TrySetResult(); Assert.True((await change).IsSuccessStatusCode);
        Assert.Equal(logout ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, (await check).StatusCode);
        Assert.Null(await f.Read(db => db.WorkContexts.Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task R04_25_Line_activity_and_issuance_are_serialized(bool lineFirst)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await LineRace(f, lineFirst); }
    internal static async Task LineRace(PersonalSessionFixture f, bool lineFirst)
    {
        var task = await Task(f); var auth = await f.Login(); var admin = await f.Login("admin");
        f.Fault.Arm(lineFirst ? "UPDATE \"ProductionLines\"" : "INSERT INTO \"WorkContexts\"");
        var issue = lineFirst ? null : f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f));
        var line = lineFirst ? f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate", new ProvisioningRevisionInput(1)) : null;
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        issue ??= f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f));
        line ??= f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/production-lines/{f.Line1}/deactivate", new ProvisioningRevisionInput(1));
        f.Fault.Release.TrySetResult(); var r = await issue; (await line).EnsureSuccessStatusCode();
        Assert.False(await f.Read(db => db.ProductionLines.Where(x => x.Id == f.Line1).Select(x => x.IsActive).SingleAsync()));
        Assert.Equal(lineFirst ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created, r.StatusCode);
        if (!lineFirst)
        {
            var c = (await r.Content.ReadFromJsonAsync<WorkContextIssued>())!;
            Assert.Equal(HttpStatusCode.OK, (await f.Client(admin.AccessToken).PostAsJsonAsync($"/api/admin/work-contexts/{c.Context.Id}/security-revoke", new WorkContextRevokeInput(1, "Inactive line"))).StatusCode);
        }
    }
}
