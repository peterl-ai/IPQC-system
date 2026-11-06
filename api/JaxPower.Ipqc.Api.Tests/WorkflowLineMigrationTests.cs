using System.Text.Json;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

// Identifiers come from fixed test/schema manifests; values remain SQL parameters.
#pragma warning disable EF1002
public sealed class WorkflowLineMigrationTests
{
    [Fact]
    public async Task Fresh_schema_enforces_normalized_codes_versions_lines_and_restrictive_foreign_keys()
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        var line = new ProductionLine { Id = Guid.NewGuid(), Code = "  test-Line  ", Name = "Test", IsActive = false };
        Assert.Equal("test-Line", line.Code); Assert.Equal("TEST-LINE", line.NormalizedCode);
        db.ProductionLines.Add(line); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal("TEST-LINE", (await db.ProductionLines.SingleAsync(x => x.Id == line.Id)).NormalizedCode);
        Assert.Throws<ArgumentException>(() => new ProductionLine { Code = " \t " });
        db.ProductionLines.Add(new ProductionLine { Id = Guid.NewGuid(), Code = "TEST-line", Name = "Duplicate inactive code" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.ProductionLines.Add(new ProductionLine { Id = Guid.NewGuid(), Code = "TEST LINE", Name = "Internal spaces are preserved" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        foreach (var table in new[] { "PatrolPlans", "PatrolTasks" })
        {
            var target = table == "PatrolPlans" ? id.Plan : id.Task;
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"WorkflowVersion\" = 99 WHERE \"Id\" = {{0}}", target));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"WorkflowVersion\" = 0 WHERE \"Id\" = {{0}}", target));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"WorkflowVersion\" = 2, \"LineId\" = NULL WHERE \"Id\" = {{0}}", target));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"LineId\" = {{0}} WHERE \"Id\" = {{1}}", Guid.NewGuid(), target));
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"LineId\" = NULL WHERE \"Id\" = {{0}}", target);
            await db.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"LineId\" = {{0}}, \"WorkflowVersion\" = 2 WHERE \"Id\" = {{1}}", id.Line, target);
        }
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ProductionLines WHERE Id = {id.Line}"));
        // Task FK independently restricts deletion, even if its Plan is linked elsewhere.
        await db.PatrolPlans.Where(x => x.Id == id.Plan).ExecuteUpdateAsync(u => u.SetProperty(x => x.LineId, line.Id));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ProductionLines WHERE Id = {id.Line}"));
    }

    [Theory]
    [InlineData("20261009193250_Phase1GLocalAuthenticationUsersRbac")]
    [InlineData("20261004191034_Phase1FCompletedRecordsReporting")]
    [InlineData("20261004001954_PatrolPlansAndScheduler")]
    public async Task Populated_historical_upgrade_preserves_every_existing_column_and_relationship(string historical)
    {
        await using var app = await WorkflowFixture.CreateAsync();
        var id = await app.SeedAsync(WorkflowVersion.LegacyPersonalV1, "Completed");
        var historicalPath = app.Path + ".historical";
        await using var historicalFile = new HistoricalFile(historicalPath);
        await using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={historicalPath}").Options);
        await db.GetService<IMigrator>().MigrateAsync(historical);
        // Copy only columns present in the historical schema; never INSERT using today's Plan model.
        await CopyHistoricalFactsAsync(app.Path, historicalPath);
        var facts = await CaptureAsync(historicalPath);
        await db.Database.MigrateAsync();
        Assert.Equal(facts, await CaptureAsync(historicalPath, historicalColumns: true));
        db.ChangeTracker.Clear();
        var plan = await db.PatrolPlans.AsNoTracking().SingleAsync(); var task = await db.PatrolTasks.AsNoTracking().SingleAsync();
        Assert.Equal(id.Plan, plan.Id); Assert.Equal(id.Task, task.Id);
        Assert.Equal(WorkflowVersion.LegacyPersonalV1, plan.WorkflowVersion); Assert.Null(plan.LineId);
        Assert.Equal(WorkflowVersion.LegacyPersonalV1, task.WorkflowVersion); Assert.Null(task.LineId);
        Assert.Equal(0, await db.ProductionLines.CountAsync());
        if (historical != "20261009193250_Phase1GLocalAuthenticationUsersRbac")
        { Assert.Null(task.AssignedInspectorUserId); Assert.All(await db.PatrolPlanAssignees.ToListAsync(), x => Assert.Null(x.UserId)); }
        if (historical == "20261004001954_PatrolPlansAndScheduler")
        { Assert.Null(task.PlanNoSnapshot); Assert.Empty(await db.PatrolTaskItems.ToListAsync()); }
        else
        {
            var report = await new JaxPower.Ipqc.Api.Services.CompletedPatrolRecordService(db).GetReportAsync(id.Task, default);
            Assert.NotNull(report); Assert.Equal("historical-inspector", report.FinalSubmittedBy);
            Assert.True(report.Items.Single().IsNa);
            Assert.Equal("historical-machine", report.Items.Single().MachineCode);
            Assert.NotEmpty(new JaxPower.Ipqc.Api.Services.CompletedPatrolReportExcel().Write(report));
        }
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class HistoricalFile(string path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }

    private static async Task CopyHistoricalFactsAsync(string source, string target)
    {
        await using var src = new SqliteConnection($"Data Source={source}"); await src.OpenAsync();
        await using var dst = new SqliteConnection($"Data Source={target}"); await dst.OpenAsync();
        foreach (var table in new[] { "ApplicationUsers", "PatrolStandards", "PatrolStandardItems", "PatrolPlans", "PatrolPlanAssignees",
            "PatrolTasks", "PatrolTaskItems", "PatrolTaskItemSamples", "PatrolTaskSubmissions", "PatrolTaskSubmissionItems", "PatrolTaskSubmissionSamples", "PatrolTaskReviews" })
        {
            await using var info = dst.CreateCommand(); info.CommandText = $"PRAGMA table_info(\"{table}\")";
            var columns = new List<string>(); await using (var r = await info.ExecuteReaderAsync()) while (await r.ReadAsync()) columns.Add(r.GetString(1));
            if (columns.Count == 0) continue;
            var names = string.Join(",", columns.Select(x => $"\"{x}\""));
            await using var read = src.CreateCommand(); read.CommandText = $"SELECT {names} FROM \"{table}\"";
            await using var rows = await read.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                await using var insert = dst.CreateCommand();
                insert.CommandText = $"INSERT INTO \"{table}\" ({names}) VALUES ({string.Join(",", columns.Select((_, i) => $"@p{i}"))})";
                for (var i = 0; i < columns.Count; i++) insert.Parameters.AddWithValue($"@p{i}", rows.GetValue(i));
                await insert.ExecuteNonQueryAsync();
            }
        }
    }

    // Capture all original columns, with deterministic ordering, without querying today's EF model.
    // Extra post-upgrade tables and columns are excluded using the remembered schema manifest.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string[]>> Schemas = new();
    private static async Task<string> CaptureAsync(string path, bool historicalColumns = false)
    {
        await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
        var schema = new Dictionary<string, string[]>();
        if (historicalColumns) schema = Schemas[path];
        else
        {
            await using var names = connection.CreateCommand(); names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            var tables = new List<string>(); await using (var r = await names.ExecuteReaderAsync()) while (await r.ReadAsync()) tables.Add(r.GetString(0));
            foreach (var table in tables)
            {
                await using var columns = connection.CreateCommand(); columns.CommandText = $"PRAGMA table_info(\"{table}\")";
                var list = new List<string>(); await using var r = await columns.ExecuteReaderAsync(); while (await r.ReadAsync()) list.Add(r.GetString(1)); schema[table] = list.ToArray();
            }
            Schemas[path] = schema;
        }
        var data = new Dictionary<string, List<string>>();
        foreach (var (table, columns) in schema)
        {
            await using var cmd = connection.CreateCommand(); cmd.CommandText = $"SELECT {string.Join(",", columns.Select(x => $"\"{x}\""))} FROM \"{table}\" ORDER BY Id";
            var rows = new List<string>(); await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? null : r.GetValue(i)).ToArray()));
            data[table] = rows;
        }
        if (historicalColumns) Schemas.TryRemove(path, out _);
        return JsonSerializer.Serialize(data);
    }

    [Fact]
    public void PostgreSQL_migration_SQL_has_provider_types_defaults_checks_indexes_and_no_history_drop()
    {
        var options = new DbContextOptionsBuilder<IpqcDbContext>().UseNpgsql("Host=localhost;Database=ipqc_sql_generation_only;Username=unused").Options;
        using var db = new IpqcDbContext(options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261009193250_Phase1GLocalAuthenticationUsersRbac", options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("\"LineId\" uuid", sql); Assert.Contains("\"WorkflowVersion\" integer NOT NULL DEFAULT 1", sql);
        Assert.Contains("\"Id\" uuid", sql); Assert.Contains("\"IsActive\" boolean", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_ProductionLines_NormalizedCode\"", sql);
        Assert.Contains("CK_PatrolPlans_Workflow", sql); Assert.Contains("CK_PatrolTasks_Workflow", sql);
        Assert.Contains("ON DELETE RESTRICT", sql); Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("UPDATE \"PatrolTaskSubmissions\"", sql);
    }
}
