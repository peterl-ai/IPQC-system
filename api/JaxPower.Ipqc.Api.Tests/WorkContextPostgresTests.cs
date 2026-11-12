using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextPostgresTests
{
    [DisposablePostgresFact] public async Task Foundation() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextTests.Foundation(f); }
    [DisposablePostgresFact] public async Task Issue_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, false); }
    [DisposablePostgresFact] public async Task Reset_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, true); }
    [DisposablePostgresFact] public async Task Role_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, true, "role"); }
    [DisposablePostgresFact] public async Task Issue_before_role_loss() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, false, "role"); }
    [DisposablePostgresFact] public async Task Deactivate_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, true, "deactivate"); }
    [DisposablePostgresFact] public async Task Issue_before_deactivation() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, false, "deactivate"); }
    [DisposablePostgresFact] public async Task Preflight_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.PreflightRace(f, false); }
    [DisposablePostgresFact] public async Task Revoke_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.PreflightRace(f, true); }
    [DisposablePostgresFact] public async Task Security() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.Security(f, "reset"); }
    [DisposablePostgresFact] public async Task Account_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.Rollback(f, "reset"); }
    [DisposablePostgresFact] public async Task Issue_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRollback(f, true); }
    [DisposablePostgresFact] public async Task Refresh_race() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.FamilyRace(f, false); }
    [DisposablePostgresFact] public async Task Logout_race() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.FamilyRace(f, true); }
    [DisposablePostgresFact] public async Task Line_race() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.LineRace(f, false); }
    [DisposablePostgresFact] public async Task Line_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.LineRace(f, true); }
    [DisposablePostgresFact] public async Task Populated_upgrade()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(true, migration: WorkContextMigrationTests.Previous);
        var task = await f.Task(); var auth = await f.Login();
        var allOldFacts = await CaptureOldPostgresFacts(f);
        var before = await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new {
            Task = await db.PatrolTasks.AsNoTracking().Select(x => new { x.Id, x.WorkflowVersion, x.LineId, x.TaskNo, x.PlanningSnapshotJson }).ToListAsync(),
            Accounts = await db.ApplicationUsers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PasswordHash, x.AuthVersion, x.Role }).ToListAsync() }));
        await f.Edit(async db => await db.Database.MigrateAsync());
        var after = await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new {
            Task = await db.PatrolTasks.AsNoTracking().Select(x => new { x.Id, x.WorkflowVersion, x.LineId, x.TaskNo, x.PlanningSnapshotJson }).ToListAsync(),
            Accounts = await db.ApplicationUsers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PasswordHash, x.AuthVersion, x.Role }).ToListAsync() }));
        Assert.Equal(before, after); Assert.Equal(0, await f.Read(db => db.WorkContexts.CountAsync()));
        Assert.Equal(allOldFacts, await CaptureOldPostgresFacts(f));
        Assert.False(await f.Read(db => System.Threading.Tasks.Task.FromResult(db.Database.HasPendingModelChanges())));
    }

    private static Task<string> CaptureOldPostgresFacts(PersonalSessionFixture f) => f.Read(async db => {
        var connection = db.Database.GetDbConnection(); await connection.OpenAsync();
        async Task<List<string>> Query(string sql) {
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
            return rows;
        }
        var tables = await Query("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT IN ('__EFMigrationsHistory','WorkContexts','WorkContextAuditEvents') ORDER BY tablename");
        var facts = new Dictionary<string, List<string>>();
        foreach (var table in tables) {
            var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
            facts[table] = await Query($"SELECT row_to_json(t)::text FROM {quoted} t ORDER BY \"Id\"");
        }
        facts["columns"] = await Query("SELECT row_to_json(c)::text FROM information_schema.columns c WHERE table_schema='public' AND table_name NOT IN ('__EFMigrationsHistory','WorkContexts','WorkContextAuditEvents') ORDER BY table_name, ordinal_position");
        facts["constraints"] = await Query("SELECT conrelid::regclass::text || ':' || conname || ':' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace='public'::regnamespace AND conrelid NOT IN ('\"__EFMigrationsHistory\"'::regclass) AND conrelid::regclass::text NOT IN ('\"WorkContexts\"','\"WorkContextAuditEvents\"') ORDER BY conrelid::regclass::text, conname");
        facts["indexes"] = await Query("SELECT indexdef FROM pg_indexes WHERE schemaname='public' AND tablename NOT IN ('__EFMigrationsHistory','WorkContexts','WorkContextAuditEvents') ORDER BY tablename,indexname");
        return System.Text.Json.JsonSerializer.Serialize(facts);
    });
}
