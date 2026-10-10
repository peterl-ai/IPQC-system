using System.Net.Http.Headers;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JaxPower.Ipqc.Api.Tests;

internal static class TestAccounts
{
    public static readonly Guid AdminId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid PqeId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Ipqa1Id = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid Ipqa2Id = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public const string Password = "TestOnlyPass123";

    public static async Task SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        if (await db.ApplicationUsers.AnyAsync()) return;
        Add(AdminId, "admin", "Admin");
        Add(PqeId, "pqe", "PQE");
        Add(Ipqa1Id, "dev-ipqa-1", "IPQA");
        Add(Ipqa2Id, "dev-ipqa-2", "IPQA");
        await db.SaveChangesAsync();
        void Add(Guid id, string name, string role)
        {
            var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
            var user = new ApplicationUser { Id = id, Username = name, NormalizedUsername = LocalAuthService.Normalize(name),
                DisplayName = name, Role = role, IsActive = true, MustChangePassword = false,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = "test", UpdatedBy = "test" };
            user.PasswordHash = hasher.HashPassword(user, Password);
            db.ApplicationUsers.Add(user);
        }
    }

    public static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginInput(username, Password, "Native"));
        response.EnsureSuccessStatusCode();
        var output = (await response.Content.ReadFromJsonAsync<AuthOutput>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", output.AccessToken);
        return client;
    }
}
