using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PersonalSessionMigrationTests
{
    public const string Previous = "20261109202320_P103ADeviceEnrollment";
    public const string Current = "20261110215126_R3202PersonalSessionReadFoundation";

    [Fact]
    public async Task Populated_latest_upgrade_preserves_all_original_refresh_enrollment_device_audit_columns()
    {
        await using var source = await EnrollmentFixture.CreateAsync();
        var device = await source.BoundDevice(); await source.Seed(device, credentialState: CandidateCredentialState.Failed, pending: true);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ipqc-r3202-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options);
            await db.GetService<IMigrator>().MigrateAsync(Previous);
            await CopyAllOldColumns(source.App.Path, path);
            var before = await WorkflowLineMigrationTests.CaptureAsync(path);
            await db.Database.MigrateAsync();
            Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path, true));
            Assert.All(await db.RefreshTokenSessions.ToListAsync(), x => { Assert.Null(x.PersonalSessionId); Assert.Null(x.PersonalSessionEnvironmentId); });
            Assert.NotEmpty(await db.RefreshTokenSessions.ToListAsync());
            var candidate = await db.DeviceCredentials.SingleAsync(); Assert.True(candidate.ExternalRevocationPending); Assert.Null(candidate.CertificatePem);
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA foreign_key_check";
            await using var rows = await check.ExecuteReaderAsync(); Assert.False(await rows.ReadAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private static async Task CopyAllOldColumns(string source, string target)
    {
        await using var connection = new SqliteConnection($"Data Source={target}"); await connection.OpenAsync();
        await using var attach = connection.CreateCommand(); attach.CommandText = "ATTACH DATABASE @source AS original"; attach.Parameters.AddWithValue("@source", source); await attach.ExecuteNonQueryAsync();
        await using var names = connection.CreateCommand(); names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'";
        var tables = new List<string>(); await using (var reader = await names.ExecuteReaderAsync()) while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        await using var constraints = connection.CreateCommand(); constraints.CommandText = "PRAGMA foreign_keys=OFF"; await constraints.ExecuteNonQueryAsync();
        foreach (var table in tables)
        {
            await using var info = connection.CreateCommand(); info.CommandText = $"PRAGMA table_info(\"{table}\")";
            var columns = new List<string>(); await using (var reader = await info.ExecuteReaderAsync()) while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            var fields = string.Join(",", columns.Select(x => $"\"{x}\""));
            await using var copy = connection.CreateCommand(); copy.CommandText = $"INSERT INTO \"{table}\" ({fields}) SELECT {fields} FROM original.\"{table}\""; await copy.ExecuteNonQueryAsync();
        }
        constraints.CommandText = "PRAGMA foreign_keys=ON"; await constraints.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Fresh_constraints_limit_one_current_family_preserve_history_and_restrict_users()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db =>
        {
            var row = await db.RefreshTokenSessions.AsNoTracking().SingleAsync(); row.Id = Guid.NewGuid(); row.TokenHash = new string('A', 64); db.RefreshTokenSessions.Add(row);
        }));
        await f.Refresh(auth.RefreshToken!);
        Assert.Equal(2, await f.Read(db => db.RefreshTokenSessions.CountAsync()));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => db.ApplicationUsers.Remove(await db.ApplicationUsers.SingleAsync(x => x.Id == TestAccounts.Ipqa1Id))));
        Assert.False(await f.Read(db => System.Threading.Tasks.Task.FromResult(db.Database.HasPendingModelChanges())));
    }

    [Fact]
    public void PostgreSQL_sql_is_additive_native_typed_and_down_cannot_destroy_personal_history()
    {
        using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript(Previous, Current, options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("\"PersonalSessionId\" uuid", sql); Assert.Contains("\"PersonalSessionEnvironmentId\" character varying(200)", sql);
        Assert.Contains("WHERE \"PersonalSessionId\" IS NOT NULL AND \"RevokedAtUtc\" IS NULL", sql);
        Assert.Contains("IX_PatrolTasks_WorkflowVersion_LineId_ScheduledOccurrenceUtc_Id", sql);
        Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("UPDATE \"", sql); Assert.DoesNotContain("CREATE TABLE \"", sql);
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(Current, Previous));
    }
}
