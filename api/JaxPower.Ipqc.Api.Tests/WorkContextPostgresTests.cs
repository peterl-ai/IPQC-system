using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextPostgresTests(ITestOutputHelper output)
{
    [DisposablePostgresFact] public async Task Snapshot_waiter_deactivation() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.WaitingSecurity(f, "deactivate", output); }
    [DisposablePostgresFact] public async Task Snapshot_waiter_role_loss() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.WaitingSecurity(f, "role", output); }
    [DisposablePostgresFact] public async Task Snapshot_waiter_password_reset() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.WaitingSecurity(f, "reset", output); }
    [DisposablePostgresFact] public async Task Unprobed_waiter_deactivation() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.WaitingSecurity(f, "deactivate", output, false); }
    [DisposablePostgresFact] public async Task Unprobed_waiter_role_loss() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.WaitingSecurity(f, "role", output, false); }
    [DisposablePostgresFact] public async Task Unprobed_waiter_password_reset() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.WaitingSecurity(f, "reset", output, false); }
    [DisposablePostgresFact] public async Task Logout_before_waiting_issuance() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.FamilyBeforeIssuance(f, true, output); }
    [DisposablePostgresFact] public async Task Refresh_before_waiting_issuance() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextRevocationRace.FamilyBeforeIssuance(f, false, output); }
    [DisposablePostgresFact] public async Task Normal_logout() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.NormalSessionChange(f, "logout"); }
    [DisposablePostgresFact] public async Task Normal_expiry() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.NormalSessionChange(f, "expiry"); }
    [DisposablePostgresFact] public async Task Normal_self_password() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.NormalSessionChange(f, "self-password"); }
    [DisposablePostgresFact] public async Task Normal_refresh() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.NormalSessionChange(f, "refresh"); }
    [DisposablePostgresFact] public async Task Deactivation_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.Rollback(f, "deactivate"); }
    [DisposablePostgresFact] public async Task Role_loss_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.Rollback(f, "role"); }
    [DisposablePostgresFact] public async Task Reactivation_stays_revoked() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.Security(f, "deactivate"); }
    [DisposablePostgresFact] public async Task Role_restoration_stays_revoked() { await using var f = await PersonalSessionFixture.CreateAsync(true); await WorkContextSecurityTests.Security(f, "role"); }
    [DisposablePostgresFact] public async Task Issuance_preserves_owner_and_session_business_values()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(true);
        var task = await WorkContextFixture.Task(f); var auth = await f.Login();
        async Task<string> Facts() => await f.Read(async db => System.Text.Json.JsonSerializer.Serialize(new {
            User = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == TestAccounts.Ipqa1Id),
            Sessions = await db.RefreshTokenSessions.AsNoTracking().Where(x => x.UserId == TestAccounts.Ipqa1Id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.TokenHash, x.AuthVersion, x.CreatedAtUtc, x.ExpiresAtUtc, x.RevokedAtUtc,
                    x.ReplacedBySessionId, x.PersonalSessionId, x.PersonalSessionEnvironmentId }).ToListAsync() }));
        var before = await Facts();
        await WorkContextFixture.Issue(f, task, auth);
        Assert.Equal(before, await Facts());
        Assert.Equal(1, await f.Read(db => db.WorkContexts.CountAsync()));
        Assert.Equal(1, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
    }
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
        var tables = await Query("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT IN ('__EFMigrationsHistory','WorkContexts','WorkContextAuditEvents','InspectionAttempts','InspectionAttemptApplicabilities','ItemRemovalOperations','SharedOperationIdentities','SharedDrafts','SharedSamples','SharedSampleProjections','ContributionOperations','ContributionDependencies','AcceptedBatchReceipts','SharedAcceptanceAudits','InspectionAttachments','AttachmentAvailabilities','AttachmentUploads','AttachmentAcceptances','AttachmentEvents','DraftOperationKeys','DraftAttachmentOperations','DraftAttachmentProjections','AcceptedOperationIdentities','UnableToMeasureOperations','UnableToMeasureProjections') ORDER BY tablename");
        var facts = new Dictionary<string, List<string>>();
        foreach (var table in tables) {
            var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
            facts[table] = await Query($"SELECT row_to_json(t)::text FROM {quoted} t ORDER BY \"Id\"");
        }
        facts["columns"] = await Query("SELECT row_to_json(c)::text FROM information_schema.columns c WHERE table_schema='public' AND table_name NOT IN ('__EFMigrationsHistory','WorkContexts','WorkContextAuditEvents','InspectionAttempts','InspectionAttemptApplicabilities','ItemRemovalOperations','SharedOperationIdentities','SharedDrafts','SharedSamples','SharedSampleProjections','ContributionOperations','ContributionDependencies','AcceptedBatchReceipts','SharedAcceptanceAudits','InspectionAttachments','AttachmentAvailabilities','AttachmentUploads','AttachmentAcceptances','AttachmentEvents','DraftOperationKeys','DraftAttachmentOperations','DraftAttachmentProjections','AcceptedOperationIdentities','UnableToMeasureOperations','UnableToMeasureProjections') ORDER BY table_name, ordinal_position");
        facts["constraints"] = await Query("SELECT conrelid::regclass::text || ':' || conname || ':' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace='public'::regnamespace AND conrelid NOT IN ('\"__EFMigrationsHistory\"'::regclass) AND replace(conrelid::regclass::text,'\"','') NOT IN ('WorkContexts','WorkContextAuditEvents','InspectionAttempts','InspectionAttemptApplicabilities','ItemRemovalOperations','SharedOperationIdentities','SharedDrafts','SharedSamples','SharedSampleProjections','ContributionOperations','ContributionDependencies','AcceptedBatchReceipts','SharedAcceptanceAudits','InspectionAttachments','AttachmentAvailabilities','AttachmentUploads','AttachmentAcceptances','AttachmentEvents','DraftOperationKeys','DraftAttachmentOperations','DraftAttachmentProjections','AcceptedOperationIdentities','UnableToMeasureOperations','UnableToMeasureProjections') ORDER BY conrelid::regclass::text, conname");
        facts["indexes"] = await Query("SELECT indexdef FROM pg_indexes WHERE schemaname='public' AND tablename NOT IN ('__EFMigrationsHistory','WorkContexts','WorkContextAuditEvents','InspectionAttempts','InspectionAttemptApplicabilities','ItemRemovalOperations','SharedOperationIdentities','SharedDrafts','SharedSamples','SharedSampleProjections','ContributionOperations','ContributionDependencies','AcceptedBatchReceipts','SharedAcceptanceAudits','InspectionAttachments','AttachmentAvailabilities','AttachmentUploads','AttachmentAcceptances','AttachmentEvents','DraftOperationKeys','DraftAttachmentOperations','DraftAttachmentProjections','AcceptedOperationIdentities','UnableToMeasureOperations','UnableToMeasureProjections') ORDER BY tablename,indexname");
        return System.Text.Json.JsonSerializer.Serialize(facts);
    });
}
