using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

// Each fact creates independent real PostgreSQL connections and real byte-backed Ready photos.
public sealed class DraftAttachmentPostgresTests
{
    private static async Task Run(Func<PhotoFixture, Task> action) { await using var p = await PhotoFixture.Create(true); await action(p); }
    [DisposablePostgresFact] public Task Lifecycle() => Run(DraftAttachmentTests.Lifecycle);
    [DisposablePostgresFact] public Task Pending() => Run(p => DraftAttachmentTests.Readiness(p, "pending"));
    [DisposablePostgresFact] public Task Missing() => Run(p => DraftAttachmentTests.Readiness(p, "missing"));
    [DisposablePostgresFact] public Task Corrupt() => Run(p => DraftAttachmentTests.Readiness(p, "corrupt"));
    [DisposablePostgresFact] public Task Unavailable() => Run(p => DraftAttachmentTests.Readiness(p, "unavailable"));
    [DisposablePostgresFact] public Task WrongItem() => Run(p => DraftAttachmentTests.Scope(p, "item"));
    [DisposablePostgresFact] public Task WrongAttempt() => Run(p => DraftAttachmentTests.Scope(p, "attempt"));
    [DisposablePostgresFact] public Task WrongClient() => Run(p => DraftAttachmentTests.Scope(p, "client"));
    [DisposablePostgresFact] public Task WrongHash() => Run(p => DraftAttachmentTests.Scope(p, "hash"));
    [DisposablePostgresFact] public Task WrongLine() => Run(p => DraftAttachmentTests.Scope(p, "line"));
    [DisposablePostgresFact] public Task WrongEnvironment() => Run(p => DraftAttachmentTests.Scope(p, "environment"));
    [DisposablePostgresFact] public Task WrongWorkflow() => Run(p => DraftAttachmentTests.Scope(p, "workflow"));
    [DisposablePostgresFact] public Task WrongTask() => Run(p => DraftAttachmentTests.Scope(p, "task"));
    [DisposablePostgresFact] public Task StaleAttempt() => Run(p => DraftAttachmentTests.Scope(p, "stale"));
    [DisposablePostgresFact] public Task Removed() => Run(p => DraftAttachmentTests.Scope(p, "removed"));
    [DisposablePostgresFact] public Task ReadyThenExplicitRevoke() => Run(p => DraftAttachmentTests.Revoked(p, "explicit"));
    [DisposablePostgresFact] public Task ReadyThenReset() => Run(p => DraftAttachmentTests.Revoked(p, "reset"));
    [DisposablePostgresFact] public Task ReadyThenRoleLoss() => Run(p => DraftAttachmentTests.Revoked(p, "role"));
    [DisposablePostgresFact] public Task ReadyThenDeactivation() => Run(p => DraftAttachmentTests.Revoked(p, "deactivate"));
    [DisposablePostgresFact] public Task NormalLogout() => Run(p => DraftAttachmentTests.Normal(p, "logout"));
    [DisposablePostgresFact] public Task NormalExpiry() => Run(p => DraftAttachmentTests.Normal(p, "expiry"));
    [DisposablePostgresFact] public Task NormalPassword() => Run(p => DraftAttachmentTests.Normal(p, "self-password"));
    [DisposablePostgresFact] public Task NormalRefresh() => Run(p => DraftAttachmentTests.Normal(p, "refresh"));
    [DisposablePostgresFact] public Task AuthorityAndShape() => Run(DraftAttachmentTests.Authority);
    [DisposablePostgresFact] public Task ChangedIdentity() => Run(DraftAttachmentTests.Conflicts);
    [DisposablePostgresFact] public Task Limits() => Run(DraftAttachmentTests.Limits);
    [DisposablePostgresFact] public Task AcceptedFrontierAndMixedQuarantine() => Run(DraftAttachmentTests.Mixed);
    [DisposablePostgresFact] public Task RevokeFirst() => Run(p => DraftAttachmentConcurrencyTests.Security(p, true, "explicit"));
    [DisposablePostgresFact] public Task AcceptBeforeRevoke() => Run(p => DraftAttachmentConcurrencyTests.Security(p, false, "explicit"));
    [DisposablePostgresFact] public Task ResetFirst() => Run(p => DraftAttachmentConcurrencyTests.Security(p, true, "reset"));
    [DisposablePostgresFact] public Task AcceptBeforeReset() => Run(p => DraftAttachmentConcurrencyTests.Security(p, false, "reset"));
    [DisposablePostgresFact] public Task RoleFirst() => Run(p => DraftAttachmentConcurrencyTests.Security(p, true, "role"));
    [DisposablePostgresFact] public Task AcceptBeforeRole() => Run(p => DraftAttachmentConcurrencyTests.Security(p, false, "role"));
    [DisposablePostgresFact] public Task DeactivateFirst() => Run(p => DraftAttachmentConcurrencyTests.Security(p, true, "deactivate"));
    [DisposablePostgresFact] public Task AcceptBeforeDeactivation() => Run(p => DraftAttachmentConcurrencyTests.Security(p, false, "deactivate"));
    [DisposablePostgresFact] public Task PhotoCas() => Run(p => DraftAttachmentConcurrencyTests.Cas(p, "photo"));
    [DisposablePostgresFact] public Task SampleCas() => Run(p => DraftAttachmentConcurrencyTests.Cas(p, "sample"));
    [DisposablePostgresFact] public Task ItemCas() => Run(p => DraftAttachmentConcurrencyTests.Cas(p, "item"));
    [DisposablePostgresFact] public Task LostResponse() => Run(DraftAttachmentConcurrencyTests.Lost);
    [DisposablePostgresFact] public Task Snapshot() => Run(DraftAttachmentConcurrencyTests.Snapshot);
    [DisposablePostgresFact] public Task CurrentActorLogoutFirst() => Run(p => DraftAttachmentConcurrencyTests.Logout(p, true));
    [DisposablePostgresFact] public Task AcceptBeforeCurrentActorLogout() => Run(p => DraftAttachmentConcurrencyTests.Logout(p, false));
    [DisposablePostgresFact] public Task ReadyReferenceLockRace() => Run(DraftAttachmentConcurrencyTests.ReadyRace);
    [DisposablePostgresFact] public Task BoundedHistory() => Run(DraftAttachmentReadTests.Paging);
    [DisposablePostgresFact] public Task UnavailableDetach() => Run(DraftAttachmentReadTests.UnavailableDetach);
    [DisposablePostgresFact] public Task RootRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "INSERT INTO \"SharedDrafts\""));
    [DisposablePostgresFact] public Task CasRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "UPDATE \"SharedDrafts\""));
    [DisposablePostgresFact] public Task BatchRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "INSERT INTO \"AcceptedBatchReceipts\""));
    [DisposablePostgresFact] public Task LedgerRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "INSERT INTO \"DraftAttachmentOperations\""));
    [DisposablePostgresFact] public Task ProjectionRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "INSERT INTO \"DraftAttachmentProjections\""));
    [DisposablePostgresFact] public Task AuditRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task CommitRollback() => Run(p => DraftAttachmentRollbackTests.Fresh(p, "commit"));
    [DisposablePostgresFact] public Task DetachProjectionRollback() => Run(p => DraftAttachmentRollbackTests.Detach(p, "UPDATE \"DraftAttachmentProjections\""));
    [DisposablePostgresFact] public Task DetachAuditRollback() => Run(p => DraftAttachmentRollbackTests.Detach(p, "INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task DetachCommitRollback() => Run(p => DraftAttachmentRollbackTests.Detach(p, "commit"));
    [DisposablePostgresFact] public Task NativeIntegrity() => Run(DraftAttachmentMigrationTests.Integrity);
    [DisposablePostgresFact] public Task PhotoIdentity() => Run(p => DraftAttachmentMigrationTests.Identities(p, "photo"));
    [DisposablePostgresFact] public Task SampleIdentity() => Run(p => DraftAttachmentMigrationTests.Identities(p, "sample"));
    [DisposablePostgresFact] public Task ItemIdentity() => Run(p => DraftAttachmentMigrationTests.Identities(p, "item"));
    [DisposablePostgresFact] public async Task PopulatedUpgrade() { await using var p = await PhotoFixture.Create(true, DraftAttachmentMigrationTests.Previous); await DraftAttachmentMigrationTests.Upgrade(p); }
    [DisposablePostgresFact] public async Task TableMigrationRollback() { await using var p = await PhotoFixture.Create(true, DraftAttachmentMigrationTests.Previous); await DraftAttachmentMigrationTests.Rollback(p, "CREATE TABLE \"DraftAttachmentProjections\""); }
    [DisposablePostgresFact] public async Task RegistryMigrationRollback() { await using var p = await PhotoFixture.Create(true, DraftAttachmentMigrationTests.Previous); await DraftAttachmentMigrationTests.Rollback(p, "INSERT INTO \"DraftOperationKeys\""); }
    [DisposablePostgresFact] public async Task TriggerMigrationRollback() { await using var p = await PhotoFixture.Create(true, DraftAttachmentMigrationTests.Previous); await DraftAttachmentMigrationTests.Rollback(p, "CREATE TRIGGER \"TR_DraftPhoto_Scope\""); }
    [DisposablePostgresFact] public async Task HistoryMigrationRollback() { await using var p = await PhotoFixture.Create(true, DraftAttachmentMigrationTests.Previous); await DraftAttachmentMigrationTests.Rollback(p, "INSERT INTO \"__EFMigrationsHistory\""); }
}
