using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ProvisioningMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_snapshot_has_no_pending_model_changes_for_either_provider(bool postgres)
    {
        var options = new DbContextOptionsBuilder<IpqcDbContext>();
        if (postgres) options.UseNpgsql("Host=localhost;Database=model_check_only;Username=unused");
        else options.UseSqlite("Data Source=:memory:");
        using var db = new IpqcDbContext(options.Options);
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var changes = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(changes.Count == 0, string.Join(Environment.NewLine, changes.Select(x => x.GetType().Name + ": " + System.Text.Json.JsonSerializer.Serialize(x, x.GetType()))));
    }

    [Fact]
    public async Task Populated_P101_upgrade_preserves_every_original_fact_and_does_not_fabricate_provisioning_history()
    {
        await using var source = await WorkflowFixture.CreateAsync();
        var ids = await source.SeedAsync(WorkflowVersion.LegacyPersonalV1, "Completed");
        var path = source.Path + ".p101";
        try
        {
            await using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options);
            await db.GetService<IMigrator>().MigrateAsync("20261106200752_P101WorkflowLineFoundation");
            await WorkflowLineMigrationTests.CopyHistoricalFactsAsync(source.Path, path);
            var before = await WorkflowLineMigrationTests.CaptureAsync(path);
            await db.Database.MigrateAsync();
            Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path, true));
            var line = await db.ProductionLines.SingleAsync(); Assert.Equal(ids.Line, line.Id); Assert.Equal(1, line.Revision);
            Assert.Empty(await db.RegisteredDevices.ToListAsync()); Assert.Empty(await db.ProvisioningAuditEvents.ToListAsync());
            var report = await new CompletedPatrolRecordService(db).GetReportAsync(ids.Task, default);
            Assert.NotNull(report); Assert.True(report.Items.Single().IsNa); Assert.Equal("historical-machine", report.Items.Single().MachineCode);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void PostgreSQL_SQL_uses_correct_types_unique_nullable_reservations_and_restrictive_history_FKs()
    {
        using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseNpgsql("Host=localhost;Database=sql_generation_only;Username=unused").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261106200752_P101WorkflowLineFoundation", options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("\"Revision\" bigint NOT NULL DEFAULT 1", sql);
        Assert.Contains("\"BindingVersion\" bigint", sql); Assert.Contains("\"Id\" uuid", sql);
        Assert.Contains("timestamp with time zone", sql); Assert.Contains("\"Status\" integer", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_RegisteredDevices_LineId\"", sql);
        Assert.Contains("CK_RegisteredDevices_Provisioning", sql); Assert.Contains("CK_ProductionLines_Revision", sql);
        Assert.Contains("OldLineId", sql); Assert.Contains("NewLineId", sql); Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("UPDATE \"PatrolTasks\"", sql);
        Assert.Equal("SELECT pg_advisory_xact_lock(49505102)", ProvisioningStore.PostgresLockSql);
    }
}
