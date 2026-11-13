namespace JaxPower.Ipqc.Api.Tests;

public sealed class ItemRemovalPostgresTests
{
    [DisposablePostgresFact] public async Task Full_environment_width() { await using var f = await PersonalSessionFixture.CreateAsync(true, environment: new string('e', 200)); await ItemRemovalIntegrityTests.LongEnvironment(f); }
    [DisposablePostgresFact] public Task Accepted_A_history_after_revoke() => Run(ItemRemovalIntegrityTests.AcceptedA);
    private static async Task Run(Func<PersonalSessionFixture, Task> action) { await using var f = await PersonalSessionFixture.CreateAsync(true); await action(f); }
    [DisposablePostgresFact] public Task Lifecycle() => Run(ItemRemovalTests.Lifecycle);
    [DisposablePostgresFact] public Task Authority() => Run(ItemRemovalTests.Authority);
    [DisposablePostgresFact] public Task Bad_proof() => Run(f => ItemRemovalTests.BadSource(f, "proof"));
    [DisposablePostgresFact] public Task Missing_proof() => Run(f => ItemRemovalTests.BadSource(f, "missing"));
    [DisposablePostgresFact] public Task Wrong_owner() => Run(f => ItemRemovalTests.BadSource(f, "owner"));
    [DisposablePostgresFact] public Task Foreign_attempt() => Run(f => ItemRemovalTests.Invalid(f, "foreign-attempt"));
    [DisposablePostgresFact] public Task Removal_CAS_first() => Run(f => ItemRemovalConcurrencyTests.Cas(f, true));
    [DisposablePostgresFact] public Task Sample_CAS_first() => Run(f => ItemRemovalConcurrencyTests.Cas(f, false));
    [DisposablePostgresFact] public Task Revoke_first() => Run(f => ItemRemovalConcurrencyTests.Security(f, true, "explicit"));
    [DisposablePostgresFact] public Task Removal_before_revoke() => Run(f => ItemRemovalConcurrencyTests.Security(f, false, "explicit"));
    [DisposablePostgresFact] public Task Reset_first() => Run(f => ItemRemovalConcurrencyTests.Security(f, true, "reset"));
    [DisposablePostgresFact] public Task Removal_before_reset() => Run(f => ItemRemovalConcurrencyTests.Security(f, false, "reset"));
    [DisposablePostgresFact] public Task Role_loss_first() => Run(f => ItemRemovalConcurrencyTests.Security(f, true, "role"));
    [DisposablePostgresFact] public Task Removal_before_role_loss() => Run(f => ItemRemovalConcurrencyTests.Security(f, false, "role"));
    [DisposablePostgresFact] public Task Deactivation_first() => Run(f => ItemRemovalConcurrencyTests.Security(f, true, "deactivate"));
    [DisposablePostgresFact] public Task Removal_before_deactivation() => Run(f => ItemRemovalConcurrencyTests.Security(f, false, "deactivate"));
    [DisposablePostgresFact] public Task Normal_logout() => Run(f => ItemRemovalConcurrencyTests.NormalA(f, "logout"));
    [DisposablePostgresFact] public Task Normal_expiry() => Run(f => ItemRemovalConcurrencyTests.NormalA(f, "expiry"));
    [DisposablePostgresFact] public Task Normal_refresh() => Run(f => ItemRemovalConcurrencyTests.NormalA(f, "refresh"));
    [DisposablePostgresFact] public Task Normal_self_password() => Run(f => ItemRemovalConcurrencyTests.NormalA(f, "self-password"));
    [DisposablePostgresFact] public Task Lost_response() => Run(ItemRemovalConcurrencyTests.LostResponse);
    [DisposablePostgresFact] public Task Native_integrity() => Run(ItemRemovalIntegrityTests.Integrity);
    [DisposablePostgresFact] public Task Canonical_redaction() => Run(ItemRemovalIntegrityTests.Canonical);
    [DisposablePostgresFact] public Task Root_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "INSERT INTO \"SharedDrafts\"", false));
    [DisposablePostgresFact] public Task CAS_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "UPDATE \"SharedDrafts\"", false));
    [DisposablePostgresFact] public Task Batch_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "INSERT INTO \"AcceptedBatchReceipts\"", false));
    [DisposablePostgresFact] public Task Ledger_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "INSERT INTO \"ItemRemovalOperations\"", false));
    [DisposablePostgresFact] public Task Audit_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "INSERT INTO \"SharedAcceptanceAudits\"", false));
    [DisposablePostgresFact] public Task Commit_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "commit", false));
    [DisposablePostgresFact] public Task Populated_audit_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "INSERT INTO \"SharedAcceptanceAudits\"", true));
    [DisposablePostgresFact] public Task Populated_commit_rollback() => Run(f => ItemRemovalConcurrencyTests.Rollback(f, "commit", true));
    [DisposablePostgresFact] public async Task Upgrade() { await using var f = await PersonalSessionFixture.CreateAsync(true, migration: ItemRemovalMigrationTests.Previous); await ItemRemovalMigrationTests.Upgrade(f); }
    [DisposablePostgresFact] public async Task Table_failure() { await using var f = await PersonalSessionFixture.CreateAsync(true, migration: ItemRemovalMigrationTests.Previous); await ItemRemovalMigrationTests.Rollback(f, "CREATE TABLE \"ItemRemovalOperations\""); }
    [DisposablePostgresFact] public async Task Index_failure() { await using var f = await PersonalSessionFixture.CreateAsync(true, migration: ItemRemovalMigrationTests.Previous); await ItemRemovalMigrationTests.Rollback(f, "INSERT INTO \"SharedOperationIdentities\""); }
    [DisposablePostgresFact] public async Task History_failure() { await using var f = await PersonalSessionFixture.CreateAsync(true, migration: ItemRemovalMigrationTests.Previous); await ItemRemovalMigrationTests.Rollback(f, "INSERT INTO \"__EFMigrationsHistory\""); }
}
