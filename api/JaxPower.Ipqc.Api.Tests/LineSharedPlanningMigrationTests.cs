using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.LineSharedPlanningFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class LineSharedPlanningMigrationTests
{
    public const string Previous = "20261110215126_R3202PersonalSessionReadFoundation";
    public const string Current = "20261111142052_R3203LineSharedPlanningSnapshots";
    [Fact]
    public async Task R03_30_Fresh_checks_partial_unique_restrict_and_revision_concurrency_token()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); f.Clock.Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var s = await Standard(f); var p = await Plan(f, s); await Generate(f);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => (await db.PatrolPlans.SingleAsync()).SharedPlanRevision = 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => (await db.PatrolTasks.SingleAsync()).AssignedInspectorUserId = TestAccounts.Ipqa1Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => (await db.PatrolTasks.SingleAsync()).PlanningSnapshotJson = null));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => (await db.PatrolTasks.SingleAsync()).PlanningSnapshotVersion = 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db => db.ProductionLines.Remove(await db.ProductionLines.SingleAsync(x => x.Id == f.Line1))));
        await Toggle(f, p, false); var second = await Plan(f, s);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Edit(async db =>
        {
            var t = await db.PatrolTasks.AsNoTracking().SingleAsync(); t.Id = Guid.NewGuid(); t.TaskNo = "Synthetic duplicate"; t.PatrolPlanId = second.Id; db.PatrolTasks.Add(t);
        }));
        await using var first = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite(f.Connection).Options);
        await using var other = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite(f.Connection).Options);
        var a = await first.PatrolPlans.SingleAsync(x => x.Id == second.Id); var b = await other.PatrolPlans.SingleAsync(x => x.Id == second.Id);
        a.SharedPlanRevision++; await first.SaveChangesAsync(); b.SharedPlanRevision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        Assert.False(first.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task R03_31_Populated_latest_upgrade_preserves_every_old_column_and_unadmitted_duplicates()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); await f.Login(); await f.Task(); await f.Task(); await f.Task(workflow: WorkflowVersion.LegacyPersonalV1);
        await f.Edit(async db => { var t = await db.PatrolTasks.Include(x => x.Items).FirstAsync(); t.Status = "Completed"; t.CurrentRevisionNo = 1;
            t.Items[0].Remarks = "Synthetic historical answer"; });
        var source = new SqliteConnectionStringBuilder(f.Connection).DataSource;
        await Upgrade(source, Previous);
    }
    [Fact]
    public async Task R03_31_Enrollment_pending_cleanup_and_audit_survive_table_rebuilds()
    {
        await using var f = await EnrollmentFixture.CreateAsync(); var device = await f.BoundDevice();
        await f.Seed(device, credentialState: CandidateCredentialState.Failed, pending: true);
        await Upgrade(f.App.Path, Previous);
    }
    [Theory]
    [InlineData("20261004001954_PatrolPlansAndScheduler")]
    [InlineData("20261009193250_Phase1GLocalAuthenticationUsersRbac")]
    public async Task R03_31_Historical_schema_upgrades_preserve_all_existing_values(string migration)
    {
        await using var f = await WorkflowFixture.CreateAsync(); await f.SeedAsync(WorkflowVersion.LegacyPersonalV1, "Completed", true);
        await Upgrade(f.Path, migration);
    }
    private static async Task Upgrade(string source, string migration)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ipqc-r3203-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options);
            await db.GetService<IMigrator>().MigrateAsync(migration);
            await CopyOldColumns(source, path);
            var before = await WorkflowLineMigrationTests.CaptureAsync(path);
            var schema = await ExistingSchema(path);
            await db.Database.MigrateAsync();
            Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path, true));
            var upgradedSchema = await ExistingSchema(path); Assert.All(schema, value => Assert.Contains(value, upgradedSchema));
            Assert.All(await db.PatrolPlans.ToListAsync(), x => Assert.Null(x.SharedPlanRevision));
            Assert.All(await db.PatrolTasks.ToListAsync(), x => { Assert.Null(x.PlanningSnapshotVersion); Assert.Null(x.PlanningSnapshotJson); });
            Assert.False(db.Database.HasPendingModelChanges());
            await using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
            await using var check = c.CreateCommand(); check.CommandText = "PRAGMA foreign_key_check";
            await using var result = await check.ExecuteReaderAsync(); Assert.False(await result.ReadAsync());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
    private static async Task CopyOldColumns(string source, string target)
    {
        await using var c = new SqliteConnection($"Data Source={target}"); await c.OpenAsync();
        await using var attach = c.CreateCommand(); attach.CommandText = "ATTACH DATABASE @path AS original"; attach.Parameters.AddWithValue("@path", source); await attach.ExecuteNonQueryAsync();
        await using var names = c.CreateCommand(); names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'";
        var tables = new List<string>(); await using (var r = await names.ExecuteReaderAsync()) while (await r.ReadAsync()) tables.Add(r.GetString(0));
        await using var fk = c.CreateCommand(); fk.CommandText = "PRAGMA foreign_keys=OFF"; await fk.ExecuteNonQueryAsync();
        foreach (var table in tables)
        {
            await using var info = c.CreateCommand(); info.CommandText = $"PRAGMA table_info(\"{table}\")";
            var columns = new List<string>(); await using (var r = await info.ExecuteReaderAsync()) while (await r.ReadAsync()) columns.Add(r.GetString(1));
            var fields = string.Join(",", columns.Select(x => $"\"{x}\""));
            await using var copy = c.CreateCommand(); copy.CommandText = $"INSERT INTO \"{table}\" ({fields}) SELECT {fields} FROM original.\"{table}\""; await copy.ExecuteNonQueryAsync();
        }
        fk.CommandText = "PRAGMA foreign_keys=ON"; await fk.ExecuteNonQueryAsync();
    }
    [Fact]
    public void R03_32_Both_models_and_postgres_native_additive_sql_preserve_history()
    {
        using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(Previous, Current, options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("\"SharedPlanRevision\" bigint", sql); Assert.Contains("\"PlanningSnapshotVersion\" integer", sql);
        Assert.Contains("\"PlanningSnapshotJson\" text", sql); Assert.Contains("CK_PatrolPlans_SharedAdmission", sql);
        Assert.Contains("IX_PatrolTasks_AdmittedLineOccurrence", sql); Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("DROP INDEX", sql);
        Assert.DoesNotContain("UPDATE \"", sql); Assert.DoesNotContain("CREATE TABLE \"", sql);
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(Current, Previous));
    }
    private static async Task<List<string>> ExistingSchema(string path)
    {
        await using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
        var values = new List<string>();
        foreach (var table in new[] { "PatrolPlans", "PatrolTasks" })
        {
            await using var fk = c.CreateCommand(); fk.CommandText = $"PRAGMA foreign_key_list(\"{table}\")";
            await using (var r = await fk.ExecuteReaderAsync()) while (await r.ReadAsync())
                values.Add(string.Join("|", Enumerable.Range(2, r.FieldCount - 2).Select(x => r.GetValue(x).ToString())));
        }
        await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT name,sql FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' AND name <> 'IX_PatrolTasks_AdmittedLineOccurrence' ORDER BY name";
        await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) values.Add(r.GetString(0) + ":" + r.GetString(1));
        return values.Order(StringComparer.Ordinal).ToList();
    }
    [Fact]
    public async Task R03_31_Rebuild_failure_before_first_commit_rolls_back_and_retry_preserves_all_data()
    {
        await using var source = await PersonalSessionFixture.CreateAsync(); await source.Task();
        var original = new SqliteConnectionStringBuilder(source.Connection).DataSource;
        var path = Path.Combine(Path.GetTempPath(), $"ipqc-r3203-interrupt-{Guid.NewGuid():N}.db");
        try
        {
            var fault = new SessionFault();
            await using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(fault).Options);
            await db.GetService<IMigrator>().MigrateAsync(Previous); await CopyOldColumns(original, path);
            var before = await WorkflowLineMigrationTests.CaptureAsync(path);
            fault.Arm("CREATE TABLE \"ef_temp_PatrolTasks\"", failure: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Database.MigrateAsync());
            Assert.DoesNotContain(Current, await db.Database.GetAppliedMigrationsAsync());
            await db.Database.MigrateAsync();
            Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path, true));
            Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
    [Fact]
    public async Task R03_31_Post_rebuild_history_failure_requires_verified_backup_restore_then_clean_retry()
    {
        await using var source = await PersonalSessionFixture.CreateAsync(); await source.Task();
        var original = new SqliteConnectionStringBuilder(source.Connection).DataSource;
        var path = Path.Combine(Path.GetTempPath(), $"ipqc-r3203-restore-{Guid.NewGuid():N}.db");
        var backup = path + ".backup";
        try
        {
            var fault = new SessionFault();
            var options = new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(fault).Options;
            string before;
            await using (var db = new IpqcDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync(Previous); await CopyOldColumns(original, path);
                before = await WorkflowLineMigrationTests.CaptureAsync(path);
                await db.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {backup}");
                fault.Arm("INSERT INTO \"__EFMigrationsHistory\"", failure: true);
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.Database.MigrateAsync());
                Assert.DoesNotContain(Current, await db.Database.GetAppliedMigrationsAsync());
                Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path, true));
                // EF's SQLite rebuild commits schema before history. Blind retry would repeat ADD COLUMN.
                Assert.True(await db.PatrolTasks.AllAsync(x => x.PlanningSnapshotVersion == null));
            }
            // Only the explicitly named synthetic temporary test database is replaced.
            SqliteConnection.ClearAllPools();
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(path));
            Assert.StartsWith("ipqc-r3203-restore-", Path.GetFileName(path));
            File.Copy(backup, path, overwrite: true);
            Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path));
            await using var restored = new IpqcDbContext(options);
            await restored.Database.MigrateAsync();
            Assert.Equal(before, await WorkflowLineMigrationTests.CaptureAsync(path, true));
            Assert.Contains(Current, await restored.Database.GetAppliedMigrationsAsync());
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); File.Delete(backup); }
    }
}
