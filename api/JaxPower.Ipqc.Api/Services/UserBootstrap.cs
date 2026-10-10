using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public static class UserBootstrap
{
    public static async Task InitializeAsync(WebApplication app)
    {
        if (app.Environment.IsEnvironment("Testing")) return;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        if (app.Environment.IsDevelopment()) await db.Database.MigrateAsync();
        if (await db.ApplicationUsers.AnyAsync()) return;
        var config = app.Configuration;
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var initial = config["BootstrapAdmin:Password"];
        if (app.Environment.IsDevelopment())
        {
            var password = config["DevelopmentSeed:Password"];
            if (string.IsNullOrEmpty(password)) return;
            if (!LocalAuthService.ValidPassword(password))
                throw new InvalidOperationException("DevelopmentSeed:Password does not meet the password policy.");
            Add("admin", "Development Admin", "Admin", false);
            Add("pqe", "Development PQE", "PQE", false);
            Add("ipqa01", "Development IPQA 01", "IPQA", false);
            Add("ipqa02", "Development IPQA 02", "IPQA", false);
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Development local accounts initialized.");
            return;
        }
        var username = config["BootstrapAdmin:Username"];
        var display = config["BootstrapAdmin:DisplayName"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(display) ||
            !LocalAuthService.ValidPassword(initial))
            throw new InvalidOperationException("Set BootstrapAdmin:Username, DisplayName, and a policy-compliant Password for the first Admin.");
        Add(username.Trim(), display.Trim(), "Admin", true);
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Initial Admin account created.");

        void Add(string usernameValue, string displayValue, string role, bool mustChange)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), Username = usernameValue,
                NormalizedUsername = LocalAuthService.Normalize(usernameValue), DisplayName = displayValue,
                Role = role, IsActive = true, MustChangePassword = mustChange,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = "Bootstrap", UpdatedBy = "Bootstrap" };
            user.PasswordHash = hasher.HashPassword(user, app.Environment.IsDevelopment()
                ? config["DevelopmentSeed:Password"]! : initial!);
            db.ApplicationUsers.Add(user);
        }
    }
}
