using JaxPower.Ipqc.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextMigrationTests
{
    public const string Previous = "20261111142052_R3203LineSharedPlanningSnapshots";
    [Fact] public async Task R04_30_Populated_upgrade_preserves_every_old_column_and_foreign_key()
    { await using var f = await PersonalSessionFixture.CreateAsync(migration: Previous); await Upgrade(f); }
    internal static async Task Upgrade(PersonalSessionFixture f)
    {
        await f.Login(); await f.Task();
        var source = new SqliteConnectionStringBuilder(f.Connection).DataSource;
        var before = await WorkflowLineMigrationTests.CaptureAsync(source);
        await f.Edit(async db => await db.Database.MigrateAsync());
        // Exclude the two new empty tables from the existing-data fingerprint.
        await using var c = new SqliteConnection(f.Connection); await c.OpenAsync();
        await using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA foreign_key_check";
        await using (var r = await cmd.ExecuteReaderAsync()) Assert.False(await r.ReadAsync());
        Assert.Equal(0, await f.Read(db => db.WorkContexts.CountAsync()));
        var after = await WorkflowLineMigrationTests.CaptureAsync(source, historicalColumns: true);
        Assert.Equal(before, after);
        Assert.False(await f.Read(db => System.Threading.Tasks.Task.FromResult(db.Database.HasPendingModelChanges())));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void R04_31_Provider_models_and_native_additive_SQL(bool postgres)
    {
        var options = new DbContextOptionsBuilder<IpqcDbContext>();
        if (postgres) options.UseNpgsql("Host=localhost;Database=model_only;Username=unused"); else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options); Assert.False(db.Database.HasPendingModelChanges());
        var current = db.Database.GetMigrations().Last();
        var migrator = db.GetService<IMigrator>(); var sql = migrator.GenerateScript(Previous, current);
        Assert.Contains("CREATE TABLE \"WorkContexts\"", sql); Assert.Contains("CREATE TABLE \"WorkContextAuditEvents\"", sql);
        Assert.DoesNotContain("ALTER TABLE", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("UPDATE \"", sql);
        if (postgres) { Assert.Contains("\"Id\" uuid", sql); Assert.Contains("timestamp with time zone", sql); }
        Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(current, Previous));
    }
}
