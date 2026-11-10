using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class DisposablePostgresFactAttribute : FactAttribute
{
    public DisposablePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IPQC_TEST_POSTGRES_CONNECTION")) ||
            Environment.GetEnvironmentVariable("IPQC_TEST_POSTGRES_DISPOSABLE") != "true")
            Skip = "SKIPPED: no explicitly opted-in disposable PostgreSQL server; model/SQLite tests are separate.";
    }
}

public sealed class PersonalSessionPostgresTests
{
    [DisposablePostgresFact]
    public async Task Real_PostgreSQL_fresh_constraints_and_duplicate_refresh_logout_race()
    { await using var f = await PersonalSessionFixture.CreateAsync(postgres: true); await PersonalSessionConcurrencyTests.RefreshLogout(f); }

    [DisposablePostgresFact]
    public async Task Real_PostgreSQL_failed_successor_rolls_back_consumption()
    { await using var f = await PersonalSessionFixture.CreateAsync(postgres: true); await PersonalSessionConcurrencyTests.RotationRollback(f); }

    [DisposablePostgresFact]
    public async Task Real_PostgreSQL_rotation_serializes_with_all_security_mutations()
    {
        foreach (var action in new[] { "reset", "role", "deactivate", "password" })
        { await using var f = await PersonalSessionFixture.CreateAsync(postgres: true); await PersonalSessionConcurrencyTests.SecurityRace(f, action); }
    }

    [DisposablePostgresFact]
    public async Task Real_PostgreSQL_populated_previous_upgrade_preserves_identity_and_null_legacy_family()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(postgres: true, migration: PersonalSessionMigrationTests.Previous);
        var token = "synthetic-old-refresh"; var id = Guid.NewGuid(); var now = f.Clock.Now.UtcDateTime; var expires = now.AddDays(1);
        await f.Edit(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RefreshTokenSessions" ("Id", "UserId", "TokenHash", "AuthVersion", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({id}, {TestAccounts.Ipqa1Id}, {LocalAuthService.TokenHash(token)}, {1}, {now}, {expires})
            """));
        await f.Edit(db => db.Database.MigrateAsync());
        var rows = await f.Read(db => db.RefreshTokenSessions.ToListAsync()); var row = Assert.Single(rows);
        Assert.Equal(id, row.Id); Assert.Null(row.PersonalSessionId); Assert.Equal(expires, row.ExpiresAtUtc);
        var refreshed = await f.Refresh(token); Assert.Null(refreshed.PersonalSessionId);
        var auth = await f.Login();
        Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(db =>
        {
            db.RefreshTokenSessions.Add(new RefreshTokenSession { Id = Guid.NewGuid(), UserId = TestAccounts.Ipqa1Id,
                PersonalSessionId = auth.PersonalSessionId, TokenHash = new string('F', 64), AuthVersion = 1, CreatedAtUtc = now, ExpiresAtUtc = expires });
            return Task.CompletedTask;
        }));
    }
}
