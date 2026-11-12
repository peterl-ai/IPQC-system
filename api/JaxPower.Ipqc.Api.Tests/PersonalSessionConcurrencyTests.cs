using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PersonalSessionConcurrencyTests
{
    [Theory]
    [InlineData(1, HttpStatusCode.NoContent)][InlineData(3, HttpStatusCode.ServiceUnavailable)]
    public async Task Transient_save_retry_is_atomic_and_exhaustion_is_not_invalid_credentials(int failures, HttpStatusCode status)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        f.Fault.ArmBusy(failures);
        var response = await f.Client(auth.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"));
        Assert.Equal(status, response.StatusCode);
        if (failures == 3)
        {
            Assert.Contains("PersonalAccessTemporarilyUnavailable", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
            Assert.NotNull((await f.Refresh(auth.RefreshToken!)).RefreshToken);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(auth.AccessToken).GetAsync("/api/me")).StatusCode);
            Assert.Equal(2, await f.Read(db => db.ApplicationUsers.Where(x => x.Id == TestAccounts.Ipqa1Id).Select(x => x.AuthVersion).SingleAsync()));
        }
    }
    [Fact]
    public async Task Pre_commit_failure_rolls_back_the_entire_replacement_graph()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await CommitRollback(f); }

    internal static async Task CommitRollback(PersonalSessionFixture f)
    {
        var auth = await f.Login(); f.CommitFault.Armed = true;
        using (var scope = f.Factory.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<LocalAuthService>().RefreshAsync(auth.RefreshToken, null, default));
        Assert.Equal(1, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.PersonalSessionId == auth.PersonalSessionId)));
        Assert.NotNull((await f.Refresh(auth.RefreshToken!)).RefreshToken);
    }
    [Fact]
    public async Task Duplicate_refresh_and_logout_race_has_one_successor_then_no_live_family()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await RefreshLogout(f); }

    internal static async Task RefreshLogout(PersonalSessionFixture f)
    {
        var auth = await f.Login();
        f.Fault.Arm("INSERT INTO \"RefreshTokenSessions\"");
        var refresh = f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(auth.RefreshToken));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Separate reader sees the old committed family, not a permanent logout gap.
        Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        var duplicate = f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(auth.RefreshToken));
        var logout = f.Client(auth.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(auth.RefreshToken));
        f.Fault.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await refresh).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await duplicate).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await logout).StatusCode);
        Assert.Equal(0, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.PersonalSessionId == auth.PersonalSessionId && x.RevokedAtUtc == null)));
        Assert.Equal(2, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.PersonalSessionId == auth.PersonalSessionId)));
    }

    [Fact]
    public async Task Failed_successor_save_rolls_back_consumption_and_can_retry_original_token()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await RotationRollback(f); }

    internal static async Task RotationRollback(PersonalSessionFixture f)
    {
        var auth = await f.Login(); f.Fault.Arm("INSERT INTO \"RefreshTokenSessions\"", failure: true);
        using (var scope = f.Factory.Services.CreateScope())
            await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<LocalAuthService>()
                .RefreshAsync(auth.RefreshToken, null, default));
        Assert.Equal(1, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.RevokedAtUtc == null)));
        Assert.NotNull((await f.Refresh(auth.RefreshToken!)).RefreshToken);
    }

    [Theory]
    [InlineData("SET \"RevokedAtUtc\"")][InlineData("\"ReplacedBySessionId\" =")]
    public async Task Failed_consumption_or_replacement_link_cannot_leave_partial_rotation(string fragment)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        f.Fault.Arm(fragment, failure: true);
        using (var scope = f.Factory.Services.CreateScope())
        {
            var error = await Record.ExceptionAsync(() => scope.ServiceProvider.GetRequiredService<LocalAuthService>().RefreshAsync(auth.RefreshToken, null, default));
            Assert.NotNull(error); Assert.Equal("Synthetic session persistence failure", error.GetBaseException().Message);
        }
        Assert.Equal(1, await f.Read(db => db.RefreshTokenSessions.CountAsync()));
        Assert.NotNull((await f.Refresh(auth.RefreshToken!)).RefreshToken);
    }

    [Fact]
    public async Task Password_reset_winning_before_login_prevents_old_password_proof_from_issuing()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        f.Fault.Arm("UPDATE \"ApplicationUsers\"");
        using var scope = f.Factory.Services.CreateScope();
        var reset = scope.ServiceProvider.GetRequiredService<ApplicationUserService>().ResetPasswordAsync(
            TestAccounts.Ipqa1Id, "SyntheticChanged123A", TestAccounts.AdminId, "admin", default);
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var oldLogin = f.Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput("dev-ipqa-1", TestAccounts.Password, "Native"));
        f.Fault.Release.TrySetResult(); await reset;
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldLogin).StatusCode);
        Assert.Equal(0, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.UserId == TestAccounts.Ipqa1Id && x.RevokedAtUtc == null)));
        var fresh = await f.Login(password: "SyntheticChanged123A"); Assert.True(fresh.User.MustChangePassword);
        Assert.NotEqual(auth.PersonalSessionId, fresh.PersonalSessionId);
    }

    [Theory]
    [InlineData("reset")][InlineData("role")][InlineData("deactivate")][InlineData("password")]
    public async Task Security_change_following_inflight_rotation_revokes_the_new_successor(string action)
    { await using var f = await PersonalSessionFixture.CreateAsync(); await SecurityRace(f, action); }

    internal static async Task SecurityRace(PersonalSessionFixture f, string action)
    {
        var auth = await f.Login(); var admin = await f.Login("admin");
        f.Fault.SessionSql.Clear();
        f.Fault.Arm("INSERT INTO \"RefreshTokenSessions\"");
        var refresh = f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(auth.RefreshToken));
        await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var client = f.Client(admin.AccessToken);
        var security = action switch
        {
            "reset" => client.PostAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}/reset-password", new ResetPasswordInput("SyntheticChanged123A")),
            "role" => client.PutAsJsonAsync($"/api/users/{TestAccounts.Ipqa1Id}", new UserUpdateInput("Synthetic changed", "PQE")),
            "deactivate" => client.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null),
            _ => f.Client(auth.AccessToken).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))
        };
        f.Fault.Release.TrySetResult();
        var rotated = await refresh; var secured = await security;
        Assert.True(rotated.IsSuccessStatusCode, $"{action}: refresh {(int)rotated.StatusCode}; {await rotated.Content.ReadAsStringAsync()}; {string.Join("\n", f.Fault.SessionSql.TakeLast(12))}");
        Assert.True(secured.IsSuccessStatusCode, $"{action}: security {(int)secured.StatusCode}; {await secured.Content.ReadAsStringAsync()}; {string.Join("\n", f.Fault.SessionSql.TakeLast(12))}");
        Assert.Equal(0, await f.Read(db => db.RefreshTokenSessions.CountAsync(x => x.UserId == TestAccounts.Ipqa1Id && x.RevokedAtUtc == null)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(auth.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(auth.RefreshToken))).StatusCode);
    }

    [Theory]
    [InlineData("reset")][InlineData("role")][InlineData("password")]
    public async Task Failed_account_save_restores_security_state_and_all_prior_authority(string action)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        var before = await f.Read(db => db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == TestAccounts.Ipqa1Id));
        f.Fault.Arm("UPDATE \"ApplicationUsers\"", failure: true);
        using (var scope = f.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<ApplicationUserService>();
            var service = scope.ServiceProvider.GetRequiredService<LocalAuthService>();
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                if (action == "reset") await users.ResetPasswordAsync(TestAccounts.Ipqa1Id, "SyntheticChanged123A", TestAccounts.AdminId, "admin", default);
                else if (action == "role") await users.UpdateAsync(TestAccounts.Ipqa1Id, new UserUpdateInput("Changed", "PQE"), TestAccounts.AdminId, "admin", default);
                else await service.ChangePasswordAsync(TestAccounts.Ipqa1Id, TestAccounts.Password, "SyntheticChanged123A", default);
            });
        }
        var after = await f.Read(db => db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == TestAccounts.Ipqa1Id));
        Assert.Equal(before.AuthVersion, after.AuthVersion); Assert.Equal(before.PasswordHash, after.PasswordHash); Assert.Equal(before.Role, after.Role);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.NotNull(await f.Refresh(auth.RefreshToken!));
    }
}
