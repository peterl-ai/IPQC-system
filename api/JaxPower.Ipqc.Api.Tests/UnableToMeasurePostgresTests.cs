using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class UnableToMeasurePostgresTests
{
    private static async Task Run(Func<PhotoFixture, Task> action) { await using var p = await PhotoFixture.Create(true); await action(p); }
    [DisposablePostgresFact] public Task Lifecycle() => Run(p => UnableToMeasureTests.Lifecycle(p));
    [DisposablePostgresFact] public Task ReasonNull() => Run(p => UnableToMeasureTests.Reason(p, null));
    [DisposablePostgresFact] public Task ReasonEmpty() => Run(p => UnableToMeasureTests.Reason(p, ""));
    [DisposablePostgresFact] public Task ReasonWhitespace() => Run(p => UnableToMeasureTests.Reason(p, "   "));
    [DisposablePostgresFact] public Task ReasonTab() => Run(p => UnableToMeasureTests.Reason(p, "\t"));
    [DisposablePostgresFact] public Task ReasonControl() => Run(p => UnableToMeasureTests.Reason(p, "control\u0001"));
    [DisposablePostgresFact] public Task ReasonBoundaryAndConflicts() => Run(p => UnableToMeasureTests.Conflicts(p));
    [DisposablePostgresFact] public Task Evidence_unassociated() => Run(p => UnableToMeasureTests.Evidence(p, "unassociated"));
    [DisposablePostgresFact] public Task Evidence_pending() => Run(p => UnableToMeasureTests.Evidence(p, "pending"));
    [DisposablePostgresFact] public Task Evidence_missing() => Run(p => UnableToMeasureTests.Evidence(p, "missing"));
    [DisposablePostgresFact] public Task Evidence_corrupt() => Run(p => UnableToMeasureTests.Evidence(p, "corrupt"));
    [DisposablePostgresFact] public Task Evidence_unavailable() => Run(p => UnableToMeasureTests.Evidence(p, "unavailable"));
    [DisposablePostgresFact] public Task Evidence_detached() => Run(p => UnableToMeasureTests.Evidence(p, "detached"));
    [DisposablePostgresFact] public Task Evidence_superseded() => Run(p => UnableToMeasureTests.Evidence(p, "superseded"));
    [DisposablePostgresFact] public Task Scope_item() => Run(p => UnableToMeasureTests.Scope(p, "item"));
    [DisposablePostgresFact] public Task Scope_attempt() => Run(p => UnableToMeasureTests.Scope(p, "attempt"));
    [DisposablePostgresFact] public Task Scope_task() => Run(p => UnableToMeasureTests.Scope(p, "task"));
    [DisposablePostgresFact] public Task Scope_line() => Run(p => UnableToMeasureTests.Scope(p, "line"));
    [DisposablePostgresFact] public Task Scope_environment() => Run(p => UnableToMeasureTests.Scope(p, "environment"));
    [DisposablePostgresFact] public Task Scope_workflow() => Run(p => UnableToMeasureTests.Scope(p, "workflow"));
    [DisposablePostgresFact] public Task Scope_hash() => Run(p => UnableToMeasureTests.Scope(p, "hash"));
    [DisposablePostgresFact] public Task Scope_stale() => Run(p => UnableToMeasureTests.Scope(p, "stale"));
    [DisposablePostgresFact] public Task Scope_removed() => Run(p => UnableToMeasureTests.Scope(p, "removed"));
    [DisposablePostgresFact] public Task Later_detach() => Run(p => UnableToMeasureTests.LaterLoss(p, "detach"));
    [DisposablePostgresFact] public Task Later_missing() => Run(p => UnableToMeasureTests.LaterLoss(p, "missing"));
    [DisposablePostgresFact] public Task Later_corrupt() => Run(p => UnableToMeasureTests.LaterLoss(p, "corrupt"));
    [DisposablePostgresFact] public Task Later_unavailable() => Run(p => UnableToMeasureTests.LaterLoss(p, "unavailable"));
    [DisposablePostgresFact] public Task SampleAndRemovedFences() => Run(p => UnableToMeasureTests.Writes(p));
    [DisposablePostgresFact] public Task Normal_logout() => Run(p => UnableToMeasureTests.Normal(p, "logout"));
    [DisposablePostgresFact] public Task Normal_expiry() => Run(p => UnableToMeasureTests.Normal(p, "expiry"));
    [DisposablePostgresFact] public Task Normal_self_password() => Run(p => UnableToMeasureTests.Normal(p, "self-password"));
    [DisposablePostgresFact] public Task Normal_refresh() => Run(p => UnableToMeasureTests.Normal(p, "refresh"));
    [DisposablePostgresFact] public Task Authority() => Run(p => UnableToMeasureTests.Authority(p));
    [DisposablePostgresFact] public Task Security_explicit_First() => Run(p => UnableToMeasureConcurrencyTests.Security(p, true, "explicit"));
    [DisposablePostgresFact] public Task Security_explicit_After() => Run(p => UnableToMeasureConcurrencyTests.Security(p, false, "explicit"));
    [DisposablePostgresFact] public Task Security_reset_First() => Run(p => UnableToMeasureConcurrencyTests.Security(p, true, "reset"));
    [DisposablePostgresFact] public Task Security_reset_After() => Run(p => UnableToMeasureConcurrencyTests.Security(p, false, "reset"));
    [DisposablePostgresFact] public Task Security_role_First() => Run(p => UnableToMeasureConcurrencyTests.Security(p, true, "role"));
    [DisposablePostgresFact] public Task Security_role_After() => Run(p => UnableToMeasureConcurrencyTests.Security(p, false, "role"));
    [DisposablePostgresFact] public Task Security_deactivate_First() => Run(p => UnableToMeasureConcurrencyTests.Security(p, true, "deactivate"));
    [DisposablePostgresFact] public Task Security_deactivate_After() => Run(p => UnableToMeasureConcurrencyTests.Security(p, false, "deactivate"));
    [DisposablePostgresFact] public Task Logout_First() => Run(p => UnableToMeasureConcurrencyTests.Logout(p, true));
    [DisposablePostgresFact] public Task Logout_After() => Run(p => UnableToMeasureConcurrencyTests.Logout(p, false));
    [DisposablePostgresFact] public Task CAS_unable() => Run(p => UnableToMeasureConcurrencyTests.Cas(p, "unable"));
    [DisposablePostgresFact] public Task CAS_sample() => Run(p => UnableToMeasureConcurrencyTests.Cas(p, "sample"));
    [DisposablePostgresFact] public Task CAS_item() => Run(p => UnableToMeasureConcurrencyTests.Cas(p, "item"));
    [DisposablePostgresFact] public Task CAS_detach() => Run(p => UnableToMeasureConcurrencyTests.Cas(p, "detach"));
    [DisposablePostgresFact] public Task LostResponse() => Run(p => UnableToMeasureConcurrencyTests.Lost(p));
    [DisposablePostgresFact] public Task PhysicalLoss_true() => Run(p => UnableToMeasureConcurrencyTests.PhysicalLoss(p, true));
    [DisposablePostgresFact] public Task PhysicalLoss_false() => Run(p => UnableToMeasureConcurrencyTests.PhysicalLoss(p, false));
    [DisposablePostgresFact] public Task FreshRollback_0() => Run(p => UnableToMeasureRollbackTests.Fresh(p, "UPDATE \"SharedDrafts\""));
    [DisposablePostgresFact] public Task FreshRollback_1() => Run(p => UnableToMeasureRollbackTests.Fresh(p, "INSERT INTO \"AcceptedBatchReceipts\""));
    [DisposablePostgresFact] public Task FreshRollback_2() => Run(p => UnableToMeasureRollbackTests.Fresh(p, "INSERT INTO \"UnableToMeasureOperations\""));
    [DisposablePostgresFact] public Task FreshRollback_3() => Run(p => UnableToMeasureRollbackTests.Fresh(p, "INSERT INTO \"UnableToMeasureProjections\""));
    [DisposablePostgresFact] public Task FreshRollback_4() => Run(p => UnableToMeasureRollbackTests.Fresh(p, "INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task FreshRollback_5() => Run(p => UnableToMeasureRollbackTests.Fresh(p, "commit"));
    [DisposablePostgresFact] public Task ClearRollback_0() => Run(p => UnableToMeasureRollbackTests.Clear(p, "UPDATE \"UnableToMeasureProjections\""));
    [DisposablePostgresFact] public Task ClearRollback_1() => Run(p => UnableToMeasureRollbackTests.Clear(p, "INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task ClearRollback_2() => Run(p => UnableToMeasureRollbackTests.Clear(p, "commit"));
    [DisposablePostgresFact] public Task NativeIntegrity() => Run(p => UnableToMeasureMigrationTests.Integrity(p));
    [DisposablePostgresFact] public Task Identity_unable() => Run(p => UnableToMeasureMigrationTests.Identities(p, "unable"));
    [DisposablePostgresFact] public Task Identity_sample() => Run(p => UnableToMeasureMigrationTests.Identities(p, "sample"));
    [DisposablePostgresFact] public Task Identity_item() => Run(p => UnableToMeasureMigrationTests.Identities(p, "item"));
    [DisposablePostgresFact] public Task Identity_photo() => Run(p => UnableToMeasureMigrationTests.Identities(p, "photo"));
    [DisposablePostgresFact] public Task FrozenMinimum() => Run(p => UnableToMeasureReadTests.Minimum(p));
    [DisposablePostgresFact] public Task BoundedHistory() => Run(p => UnableToMeasureReadTests.Paging(p));
    [DisposablePostgresFact] public Task Queued_waiter_has_an_indirect_real_blocking_path() => Run(PostgresBlocking.QueuedWaiterRegression);
    [DisposablePostgresFact] public async Task PopulatedUpgrade() { await using var p = await PhotoFixture.Create(true, UnableToMeasureMigrationTests.Previous); await UnableToMeasureMigrationTests.Upgrade(p); }
    [DisposablePostgresFact] public async Task MigrationRollback_0() { await using var p = await PhotoFixture.Create(true, UnableToMeasureMigrationTests.Previous); await UnableToMeasureMigrationTests.Rollback(p, "CREATE TABLE \"UnableToMeasureProjections\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_1() { await using var p = await PhotoFixture.Create(true, UnableToMeasureMigrationTests.Previous); await UnableToMeasureMigrationTests.Rollback(p, "INSERT INTO \"AcceptedOperationIdentities\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_2() { await using var p = await PhotoFixture.Create(true, UnableToMeasureMigrationTests.Previous); await UnableToMeasureMigrationTests.Rollback(p, "CREATE TRIGGER \"TR_Unable_Scope\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_3() { await using var p = await PhotoFixture.Create(true, UnableToMeasureMigrationTests.Previous); await UnableToMeasureMigrationTests.Rollback(p, "INSERT INTO \"__EFMigrationsHistory\""); }
    [DisposablePostgresFact] public async Task FullEnvironmentWidth() {
        await using var p = await PhotoFixture.Create(true, environment: new string('e', 200)); var f = p.F;
        var task = await WorkContextFixture.Task(f); var a = await f.Login(); var b = await f.Login("dev-ipqa-2");
        var scopes = (await f.Client(b.AccessToken).GetFromJsonAsync<InspectionIdentityOutput>(InspectionIdentityTests.Root(task)))!;
        var issued = await f.Client(a.AccessToken).PostAsJsonAsync(WorkContextFixture.Root(task), new WorkContextIssueInput(f.Line1, 2, f.Options.EnvironmentId!)); issued.EnsureSuccessStatusCode();
        var context = (await issued.Content.ReadFromJsonAsync<WorkContextIssued>())!; var item = scopes.Items[0];
        var s = new DraftSetup(f, task, item.TaskItemId, item.InspectionAttemptId, a, b, context); var bytes = PhotoFixture.Image();
        var photo = await PhotoFixture.Reserve(s, PhotoFixture.Input(s, bytes)); photo = await PhotoFixture.Ready(s, photo.AttachmentId, bytes);
        var reference = Assert.Single((await DraftAttachmentTests.Accept(s, DraftAttachmentTests.Input(s, photo))).Operations); var u = new UnableSetup(s, photo, reference);
        var receipt = (await UnableToMeasureTests.Accept(u, UnableToMeasureTests.Input(u))).Operation; Assert.Equal(f.Options.EnvironmentId, receipt.EnvironmentId);
    }
}
