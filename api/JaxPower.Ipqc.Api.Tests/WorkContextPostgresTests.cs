using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextPostgresTests
{
    [DisposablePostgresFact] public async Task Foundation() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextTests.Foundation(f); }
    [DisposablePostgresFact] public async Task Issue_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, false); }
    [DisposablePostgresFact] public async Task Reset_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextConcurrencyTests.IssueRace(f, true); }
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
        var before = await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new {
            Task = await db.PatrolTasks.AsNoTracking().Select(x => new { x.Id, x.WorkflowVersion, x.LineId, x.TaskNo, x.PlanningSnapshotJson }).ToListAsync(),
            Accounts = await db.ApplicationUsers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PasswordHash, x.AuthVersion, x.Role }).ToListAsync() }));
        await f.Edit(async db => await db.Database.MigrateAsync());
        var after = await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new {
            Task = await db.PatrolTasks.AsNoTracking().Select(x => new { x.Id, x.WorkflowVersion, x.LineId, x.TaskNo, x.PlanningSnapshotJson }).ToListAsync(),
            Accounts = await db.ApplicationUsers.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PasswordHash, x.AuthVersion, x.Role }).ToListAsync() }));
        Assert.Equal(before, after); Assert.Equal(0, await f.Read(db => db.WorkContexts.CountAsync()));
        Assert.False(await f.Read(db => System.Threading.Tasks.Task.FromResult(db.Database.HasPendingModelChanges())));
    }
}
