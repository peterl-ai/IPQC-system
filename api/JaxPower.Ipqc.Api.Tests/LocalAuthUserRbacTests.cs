using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LocalAuthUserRbacTests
{
    [Fact]
    public async Task Password_hash_login_lockout_and_me_are_secure()
    {
        await using var app = await Fixture.CreateAsync();
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var admin = await db.ApplicationUsers.SingleAsync(x => x.Username == "admin");
        var pqe = await db.ApplicationUsers.SingleAsync(x => x.Username == "pqe");
        Assert.NotEqual(TestAccounts.Password, admin.PasswordHash);
        Assert.NotEqual(admin.PasswordHash, pqe.PasswordHash);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(admin, admin.PasswordHash, TestAccounts.Password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(admin, admin.PasswordHash, "WrongPassword123"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput("unknown", "WrongPassword123", "Native"))).StatusCode);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", "WrongPassword123", "Native"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", TestAccounts.Password, "Native"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Admin.GetAsync("/api/me")).StatusCode);
        var me = await app.Admin.GetFromJsonAsync<CurrentUserOutput>("/api/me");
        Assert.Equal("admin", me!.Username);
        Assert.Equal("Admin", me.Role);
        Assert.Contains("Users.Write", me.Permissions);
        Assert.DoesNotContain("passwordHash", await (await app.Admin.GetAsync("/api/me")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_rotation_logout_and_reset_revoke_sessions()
    {
        await using var app = await Fixture.CreateAsync();
        var login = await app.LoginAsync("pqe");
        var old = login.RefreshToken!;
        Assert.NotEqual(old, (await app.GetStoredRefreshHashAsync("pqe")));
        var rotated = await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(old));
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var rotatedOutput = (await rotated.Content.ReadFromJsonAsync<AuthOutput>())!;
        var next = rotatedOutput.RefreshToken!;
        Assert.NotEqual(old, next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(old))).StatusCode);
        var client = app.ClientWithToken(rotatedOutput.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new RefreshInput(next))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(next))).StatusCode);
        var newLogin = await app.LoginAsync("pqe");
        var reset = await app.Admin.PostAsJsonAsync($"/api/users/{TestAccounts.PqeId}/reset-password",
            new ResetPasswordInput("ResetPass456A"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var oldClient = app.ClientWithToken(newLogin.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(newLogin.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput("pqe", TestAccounts.Password, "Native"))).StatusCode);
        var fresh = await app.LoginAsync("pqe", "ResetPass456A");
        Assert.True(fresh.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.ClientWithToken(fresh.AccessToken).GetAsync("/api/patrol-standards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.ClientWithToken(fresh.AccessToken).GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Password_change_requires_current_password_and_new_login()
    {
        await using var app = await Fixture.CreateAsync();
        var created = await app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("newipqa", "New IPQA", "IPQA", "InitialPass123", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var login = await app.LoginAsync("newipqa", "InitialPass123");
        Assert.True(login.User.MustChangePassword);
        var client = app.ClientWithToken(login.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordInput("wrong", "ChangedPass123"))).StatusCode);
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(x => x.Username == "newipqa");
            var version = user.AuthVersion;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password",
                new ChangePasswordInput("InitialPass123", "InitialPass123"))).StatusCode);
            await db.Entry(user).ReloadAsync();
            Assert.Equal(version, user.AuthVersion);
            Assert.True(user.MustChangePassword);
            Assert.Equal(1, await db.RefreshTokenSessions.CountAsync(x => x.UserId == user.Id && x.RevokedAtUtc == null));
        }
        Assert.True((await client.GetFromJsonAsync<CurrentUserOutput>("/api/me"))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/completed-patrol-records")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordInput("InitialPass123", "ChangedPass123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/login",
            new LoginInput("newipqa", "InitialPass123", "Native"))).StatusCode);
        Assert.False((await app.LoginAsync("newipqa", "ChangedPass123")).User.MustChangePassword);
    }

    [Fact]
    public async Task User_management_sqlite_timestamps_are_utc_in_every_response()
    {
        await using var app = await Fixture.CreateAsync();
        var created = await app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("utcuser", "UTC User", "IPQA", "InitialPass123", true));
        var row = await AssertUtcUserAsync(created);
        var id = row.Id;
        await AssertUtcUserAsync(await app.Admin.GetAsync($"/api/users/{id}"));
        var list = await app.Admin.GetAsync("/api/users?username=utcuser");
        list.EnsureSuccessStatusCode();
        var raw = await list.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(raw))
        {
            var item = document.RootElement.GetProperty("items")[0];
            AssertUtcJson(item, false);
        }
        var page = JsonSerializer.Deserialize<PagedUsers>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(DateTimeKind.Utc, Assert.Single(page.Items).CreatedAtUtc.Kind);
        await AssertUtcUserAsync(await app.Admin.PutAsJsonAsync($"/api/users/{id}", new UserUpdateInput("Updated", "IPQA")));
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput("utcuser", "WrongPass123", "Native"))).StatusCode);
        await AssertUtcUserAsync(await app.Admin.GetAsync($"/api/users/{id}"), true);
        await AssertUtcUserAsync(await app.Admin.PostAsync($"/api/users/{id}/deactivate", null), true);
        await AssertUtcUserAsync(await app.Admin.PostAsync($"/api/users/{id}/activate", null), true);
        await AssertUtcUserAsync(await app.Admin.PostAsync($"/api/users/{id}/unlock", null));
        await AssertUtcUserAsync(await app.Admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new ResetPasswordInput("ResetPass456A")));
    }

    private static async Task<UserOutput> AssertUtcUserAsync(HttpResponseMessage response, bool lockout = false)
    {
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        AssertUtcJson(document.RootElement, lockout);
        var output = JsonSerializer.Deserialize<UserOutput>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(DateTimeKind.Utc, output.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, output.UpdatedAtUtc.Kind);
        if (lockout) Assert.Equal(DateTimeKind.Utc, output.LockoutEndUtc!.Value.Kind);
        return output;
    }

    private static void AssertUtcJson(JsonElement item, bool lockout)
    {
        Assert.EndsWith("Z", item.GetProperty("createdAtUtc").GetString());
        Assert.EndsWith("Z", item.GetProperty("updatedAtUtc").GetString());
        if (lockout) Assert.EndsWith("Z", item.GetProperty("lockoutEndUtc").GetString());
    }

    [Fact]
    public async Task User_management_enforces_unique_names_role_last_admin_and_dependencies()
    {
        await using var app = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Pqe.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/patrol-standards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/patrol-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.PostAsync($"/api/patrol-tasks/{Guid.NewGuid()}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Ipqa.GetAsync("/api/completed-patrol-records")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync("/api/users",
            new UserCreateInput(" ADMIN ", "Duplicate", "Admin", "InitialPass123", true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Admin.PostAsJsonAsync("/api/users",
            new UserCreateInput("bad", "Bad", "Supervisor", "weak", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsync($"/api/users/{TestAccounts.AdminId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PutAsJsonAsync($"/api/users/{TestAccounts.AdminId}",
            new UserUpdateInput("Admin", "PQE"))).StatusCode);
        var extra = await app.Admin.PostAsJsonAsync("/api/users",
            new UserCreateInput("admin2", "Second Admin", "Admin", "InitialPass123", true));
        var extraId = (await extra.Content.ReadFromJsonAsync<UserOutput>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await app.Admin.PostAsync($"/api/users/{extraId}/deactivate", null)).StatusCode);
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var now = DateTime.UtcNow;
            var standard = new PatrolStandard { Id = Guid.NewGuid(), PatrolStandardName = "Dependency Standard",
                LineName = "Line", CreatedAtUtc = now, UpdatedAtUtc = now };
            db.PatrolStandards.Add(standard);
            db.PatrolPlans.Add(new PatrolPlan { Id = Guid.NewGuid(), PlanNo = "DEPENDENCY", PlanName = "Dependency",
                PatrolStandardId = standard.Id, IsEnabled = true, EffectiveStartUtc = now,
                ScheduleDefinition = "{}", GenerationNotBeforeUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now,
                Assignees = [new PatrolPlanAssignee { Id = Guid.NewGuid(), UserId = TestAccounts.Ipqa1Id,
                    AssigneeKey = "dev-ipqa-1", IsActive = true }] });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Migration_supports_upgrade_and_model_matches()
    {
        await using var app = await Fixture.CreateAsync();
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261009193250_Phase1GLocalAuthenticationUsersRbac", await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(4, await db.ApplicationUsers.CountAsync());
        var unique = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type='index' AND name='IX_ApplicationUsers_NormalizedUsername'").SingleAsync();
        Assert.Equal("IX_ApplicationUsers_NormalizedUsername", unique);
    }

    [Fact]
    public async Task Sqlite_phase_1f_to_1g_upgrade_adds_identity_tables_and_nullable_legacy_links()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite(connection).Options;
        await using var db = new IpqcDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261004191034_Phase1FCompletedRecordsReporting");
        Assert.DoesNotContain("20261009193250_Phase1GLocalAuthenticationUsersRbac", await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20261009193250_Phase1GLocalAuthenticationUsersRbac", applied);
        Assert.Equal(0, await db.ApplicationUsers.CountAsync());
        var columns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('PatrolTasks') WHERE name='AssignedInspectorUserId'").ToListAsync();
        Assert.Single(columns);
        columns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('PatrolPlanAssignees') WHERE name='UserId'").ToListAsync();
        Assert.Single(columns);
    }

    [Fact]
    public async Task Access_tokens_reject_malformed_expired_and_changed_accounts()
    {
        await using var app = await Fixture.CreateAsync();
        var login = await app.LoginAsync("pqe");
        Assert.Equal(HttpStatusCode.OK, (await app.ClientWithToken(login.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientWithToken("not-a-jwt").GetAsync("/api/me")).StatusCode);
        using (var scope = app.Factory.Services.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<JwtSettings>();
            var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience,
                [new Claim("sub", TestAccounts.PqeId.ToString()), new Claim("auth_version", "0")],
                expires: DateTime.UtcNow.AddMinutes(-2),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(settings.Key), SecurityAlgorithms.HmacSha256));
            Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientWithToken(new JwtSecurityTokenHandler().WriteToken(jwt)).GetAsync("/api/me")).StatusCode);
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(x => x.Id == TestAccounts.PqeId);
            user.AuthVersion++;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientWithToken(login.AccessToken).GetAsync("/api/me")).StatusCode);
        var fresh = await app.LoginAsync("pqe");
        Assert.Equal(HttpStatusCode.OK, (await app.ClientWithToken(fresh.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Admin.PostAsync($"/api/users/{TestAccounts.PqeId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientWithToken(fresh.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(fresh.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task User_crud_permissions_and_audit_use_real_username()
    {
        await using var app = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.Pqe.GetAsync("/api/patrol-standards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Pqe.GetAsync("/api/patrol-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Pqe.GetAsync("/api/completed-patrol-records")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Pqe.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/patrol-tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Ipqa.GetAsync("/api/user-lookups/ipqa")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await app.Admin.DeleteAsync($"/api/users/{TestAccounts.Ipqa1Id}")).StatusCode);
        var created = await app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("caseUser", "First Name", "PQE", "InitialPass123", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<UserOutput>())!;
        Assert.Equal("admin", row.CreatedBy);
        Assert.Equal("admin", row.UpdatedBy);
        Assert.True(row.MustChangePassword);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("CASEUSER", "Duplicate", "IPQA", "InitialPass123", true))).StatusCode);
        var page = (await app.Admin.GetFromJsonAsync<PagedUsers>("/api/users?username=case&page=1&pageSize=1"))!;
        Assert.Single(page.Items);
        Assert.Equal(row.Id, page.Items[0].Id);
        var edited = await app.Admin.PutAsJsonAsync($"/api/users/{row.Id}", new UserUpdateInput("Second Name", "IPQA"));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var updated = (await edited.Content.ReadFromJsonAsync<UserOutput>())!;
        Assert.Equal("caseUser", updated.Username);
        Assert.Equal("Second Name", updated.DisplayName);
        Assert.Equal("IPQA", updated.Role);
        Assert.Equal("admin", updated.UpdatedBy);
    }

    [Fact]
    public async Task Refresh_expiry_and_forced_password_change_block_direct_business_access()
    {
        await using var app = await Fixture.CreateAsync();
        var created = await app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("forced", "Forced", "PQE", "InitialPass123", true));
        var id = (await created.Content.ReadFromJsonAsync<UserOutput>())!.Id;
        var login = await app.LoginAsync("forced", "InitialPass123");
        var client = app.ClientWithToken(login.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patrol-standards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patrol-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/completed-patrol-records")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            var session = await db.RefreshTokenSessions.SingleAsync(x => x.UserId == id);
            session.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(login.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput("InitialPass123", "ChangedPass123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        var next = await app.LoginAsync("forced", "ChangedPass123");
        Assert.False(next.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await app.ClientWithToken(next.AccessToken).GetAsync("/api/patrol-standards")).StatusCode);
    }

    [Fact]
    public async Task Every_unfinished_task_status_blocks_ipqa_deactivation_but_completed_history_does_not()
    {
        await using var app = await Fixture.CreateAsync();
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var now = DateTime.UtcNow;
        var standard = new PatrolStandard { Id = Guid.NewGuid(), PatrolStandardName = "Status dependency",
            LineName = "Line", CreatedAtUtc = now, UpdatedAtUtc = now };
        var plan = new PatrolPlan { Id = Guid.NewGuid(), PlanNo = "STATUS-DEPENDENCY", PlanName = "Status",
            PatrolStandardId = standard.Id, IsEnabled = false, EffectiveStartUtc = now,
            ScheduleDefinition = "{}", GenerationNotBeforeUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PatrolStandards.Add(standard);
        db.PatrolPlans.Add(plan);
        var statuses = new[] { "PendingInspection", "InProgress", "PendingApproval", "Rejected" };
        var tasks = statuses.Select((status, index) => new PatrolTask { Id = Guid.NewGuid(), TaskNo = $"DEPENDENCY-{index}",
            PatrolPlanId = plan.Id, PatrolStandardId = standard.Id, AssignedInspectorUserId = TestAccounts.Ipqa1Id,
            AssignedInspectorKey = "dev-ipqa-1", ScheduledOccurrenceUtc = now.AddMinutes(index),
            GeneratedAtUtc = now, Status = status, CreatedAtUtc = now, UpdatedAtUtc = now }).ToList();
        db.PatrolTasks.AddRange(tasks);
        await db.SaveChangesAsync();
        foreach (var task in tasks)
        {
            Assert.Equal(HttpStatusCode.Conflict, (await app.Admin.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null)).StatusCode);
            task.Status = "Completed";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await app.Admin.PostAsync($"/api/users/{TestAccounts.Ipqa1Id}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Role_change_revokes_old_token_and_new_login_uses_database_role()
    {
        await using var app = await Fixture.CreateAsync();
        var old = await app.LoginAsync("pqe");
        Assert.Equal(HttpStatusCode.Forbidden, (await app.ClientWithToken(old.AccessToken).GetAsync("/api/users")).StatusCode);
        var update = await app.Admin.PutAsJsonAsync($"/api/users/{TestAccounts.PqeId}", new UserUpdateInput("Promoted", "Admin"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientWithToken(old.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(old.RefreshToken))).StatusCode);
        var fresh = await app.LoginAsync("pqe");
        Assert.Equal("Admin", fresh.User.Role);
        Assert.Equal(HttpStatusCode.OK, (await app.ClientWithToken(fresh.AccessToken).GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_duplicate_user_creates_resolve_as_created_and_conflict()
    {
        await using var app = await Fixture.CreateAsync();
        var first = app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("race-user", "First", "IPQA", "InitialPass123", true));
        var second = app.Admin.PostAsJsonAsync("/api/users", new UserCreateInput("RACE-USER", "Second", "IPQA", "InitialPass123", true));
        var results = await Task.WhenAll(first, second);
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(x => x.StatusCode).OrderBy(x => (int)x).ToArray());
    }

    private sealed class Fixture(WebApplicationFactory<Program> factory, string path, HttpClient admin,
        HttpClient pqe, HttpClient ipqa, HttpClient anonymous) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public HttpClient Admin { get; } = admin;
        public HttpClient Pqe { get; } = pqe;
        public HttpClient Ipqa { get; } = ipqa;
        public HttpClient Anonymous { get; } = anonymous;
        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ipqc-auth-{Guid.NewGuid():N}.db");
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureLogging(x => x.ClearProviders());
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Scheduler:Enabled"] = "false" }));
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                    services.AddDbContext<IpqcDbContext>(options => options.UseSqlite($"Data Source={path}"));
                });
            });
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
            await TestAccounts.SeedAsync(factory);
            return new Fixture(factory, path, await TestAccounts.ClientAsync(factory, "admin"),
                await TestAccounts.ClientAsync(factory, "pqe"),
                await TestAccounts.ClientAsync(factory, "dev-ipqa-1"), factory.CreateClient());
        }
        public async Task<AuthOutput> LoginAsync(string username, string password = TestAccounts.Password)
        {
            var response = await Anonymous.PostAsJsonAsync("/api/auth/login", new LoginInput(username, password, "Native"));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AuthOutput>())!;
        }
        public HttpClient ClientWithToken(string token)
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
        public async Task<string?> GetStoredRefreshHashAsync(string username)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
            return await db.RefreshTokenSessions.Where(x => x.User.Username == username)
                .OrderByDescending(x => x.CreatedAtUtc).Select(x => x.TokenHash).FirstOrDefaultAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose(); Pqe.Dispose(); Ipqa.Dispose(); Anonymous.Dispose();
            await Factory.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
