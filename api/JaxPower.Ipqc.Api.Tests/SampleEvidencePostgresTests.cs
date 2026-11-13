using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class SampleEvidencePostgresTests
{
    private static async Task Run(Func<PhotoFixture, Task> action) { await using var p = await PhotoFixture.Create(true); await action(p); }
    [DisposablePostgresFact] public Task Lifecycle() => Run(p => SampleEvidenceTests.Lifecycle(p));
    [DisposablePostgresFact] public Task Amendments() => Run(p => SampleEvidenceTests.Amendments(p));
    [DisposablePostgresFact] public Task Identities() => Run(p => SampleEvidenceTests.Identities(p));
    [DisposablePostgresFact] public Task Capacity() => Run(p => SampleEvidenceTests.Capacity(p));
    [DisposablePostgresFact] public Task WithinBatch() => Run(p => SampleEvidenceTests.WithinBatch(p));
    [DisposablePostgresFact] public Task Authority() => Run(p => SampleEvidenceTests.Authority(p));
    [DisposablePostgresFact] public Task Paging() => Run(p => SampleEvidenceTests.Paging(p));
    [DisposablePostgresFact] public Task Unavailable() => Run(p => SampleEvidenceTests.Unavailable(p));
    [DisposablePostgresFact] public Task Scope_0() => Run(p => SampleEvidenceTests.Scope(p, "unaccepted"));
    [DisposablePostgresFact] public Task Scope_1() => Run(p => SampleEvidenceTests.Scope(p, "hash"));
    [DisposablePostgresFact] public Task Scope_2() => Run(p => SampleEvidenceTests.Scope(p, "item"));
    [DisposablePostgresFact] public Task Scope_3() => Run(p => SampleEvidenceTests.Scope(p, "attempt"));
    [DisposablePostgresFact] public Task Scope_4() => Run(p => SampleEvidenceTests.Scope(p, "task"));
    [DisposablePostgresFact] public Task Scope_5() => Run(p => SampleEvidenceTests.Scope(p, "line"));
    [DisposablePostgresFact] public Task Scope_6() => Run(p => SampleEvidenceTests.Scope(p, "environment"));
    [DisposablePostgresFact] public Task Scope_7() => Run(p => SampleEvidenceTests.Scope(p, "workflow"));
    [DisposablePostgresFact] public Task Scope_8() => Run(p => SampleEvidenceTests.Scope(p, "stale"));
    [DisposablePostgresFact] public Task Scope_9() => Run(p => SampleEvidenceTests.Scope(p, "removed"));
    [DisposablePostgresFact] public Task Scope_10() => Run(p => SampleEvidenceTests.Scope(p, "unable"));
    [DisposablePostgresFact] public Task Photo_0() => Run(p => SampleEvidenceTests.Photo(p, "unassociated"));
    [DisposablePostgresFact] public Task Photo_1() => Run(p => SampleEvidenceTests.Photo(p, "pending"));
    [DisposablePostgresFact] public Task Photo_2() => Run(p => SampleEvidenceTests.Photo(p, "missing"));
    [DisposablePostgresFact] public Task Photo_3() => Run(p => SampleEvidenceTests.Photo(p, "corrupt"));
    [DisposablePostgresFact] public Task Photo_4() => Run(p => SampleEvidenceTests.Photo(p, "unavailable"));
    [DisposablePostgresFact] public Task Photo_5() => Run(p => SampleEvidenceTests.Photo(p, "detached"));
    [DisposablePostgresFact] public Task Photo_6() => Run(p => SampleEvidenceTests.Photo(p, "superseded"));
    [DisposablePostgresFact] public Task Photo_7() => Run(p => SampleEvidenceTests.Photo(p, "hash"));
    [DisposablePostgresFact] public Task Photo_8() => Run(p => SampleEvidenceTests.Photo(p, "unaccepted"));
    [DisposablePostgresFact] public Task Photo_9() => Run(p => SampleEvidenceTests.Photo(p, "other-item"));
    [DisposablePostgresFact] public Task Unsupported_0() => Run(p => SampleEvidenceTests.Unsupported(p, "OK-capture"));
    [DisposablePostgresFact] public Task Unsupported_1() => Run(p => SampleEvidenceTests.Unsupported(p, "OK-OK"));
    [DisposablePostgresFact] public Task Unsupported_2() => Run(p => SampleEvidenceTests.Unsupported(p, "remove"));
    [DisposablePostgresFact] public Task Source_0() => Run(p => SampleEvidenceTests.Source(p, "logout"));
    [DisposablePostgresFact] public Task Source_1() => Run(p => SampleEvidenceTests.Source(p, "expiry"));
    [DisposablePostgresFact] public Task Source_2() => Run(p => SampleEvidenceTests.Source(p, "self-password"));
    [DisposablePostgresFact] public Task Source_3() => Run(p => SampleEvidenceTests.Source(p, "refresh"));
    [DisposablePostgresFact] public Task Source_4() => Run(p => SampleEvidenceTests.Source(p, "explicit"));
    [DisposablePostgresFact] public Task Source_5() => Run(p => SampleEvidenceTests.Source(p, "reset"));
    [DisposablePostgresFact] public Task Source_6() => Run(p => SampleEvidenceTests.Source(p, "role"));
    [DisposablePostgresFact] public Task Source_7() => Run(p => SampleEvidenceTests.Source(p, "deactivate"));
    [DisposablePostgresFact] public Task Later_0() => Run(p => SampleEvidenceTests.Later(p, "removed"));
    [DisposablePostgresFact] public Task Later_1() => Run(p => SampleEvidenceTests.Later(p, "unable"));
    [DisposablePostgresFact] public Task Later_2() => Run(p => SampleEvidenceTests.Later(p, "detach"));
    [DisposablePostgresFact] public Task Transition_NGOK() => Run(p => SampleEvidenceTests.Transition(p, "NG", "OK"));
    [DisposablePostgresFact] public Task Transition_NGNG() => Run(p => SampleEvidenceTests.Transition(p, "NG", "NG"));
    [DisposablePostgresFact] public Task Transition_OKNG() => Run(p => SampleEvidenceTests.Transition(p, "OK", "NG"));
    [DisposablePostgresFact] public Task Security_true_explicit() => Run(p => SampleEvidenceConcurrencyTests.Security(p, true, "explicit"));
    [DisposablePostgresFact] public Task Security_true_reset() => Run(p => SampleEvidenceConcurrencyTests.Security(p, true, "reset"));
    [DisposablePostgresFact] public Task Security_true_role() => Run(p => SampleEvidenceConcurrencyTests.Security(p, true, "role"));
    [DisposablePostgresFact] public Task Security_true_deactivate() => Run(p => SampleEvidenceConcurrencyTests.Security(p, true, "deactivate"));
    [DisposablePostgresFact] public Task Security_false_explicit() => Run(p => SampleEvidenceConcurrencyTests.Security(p, false, "explicit"));
    [DisposablePostgresFact] public Task Security_false_reset() => Run(p => SampleEvidenceConcurrencyTests.Security(p, false, "reset"));
    [DisposablePostgresFact] public Task Security_false_role() => Run(p => SampleEvidenceConcurrencyTests.Security(p, false, "role"));
    [DisposablePostgresFact] public Task Security_false_deactivate() => Run(p => SampleEvidenceConcurrencyTests.Security(p, false, "deactivate"));
    [DisposablePostgresFact] public Task Cas_binding() => Run(p => SampleEvidenceConcurrencyTests.Cas(p, "binding"));
    [DisposablePostgresFact] public Task Cas_sample() => Run(p => SampleEvidenceConcurrencyTests.Cas(p, "sample"));
    [DisposablePostgresFact] public Task Cas_removed() => Run(p => SampleEvidenceConcurrencyTests.Cas(p, "removed"));
    [DisposablePostgresFact] public Task Cas_photo() => Run(p => SampleEvidenceConcurrencyTests.Cas(p, "photo"));
    [DisposablePostgresFact] public Task Cas_unable() => Run(p => SampleEvidenceConcurrencyTests.Cas(p, "unable"));
    [DisposablePostgresFact] public Task LaterWrite_true_sample() => Run(p => SampleEvidenceConcurrencyTests.LaterWrite(p, true, "sample"));
    [DisposablePostgresFact] public Task LaterWrite_true_detach() => Run(p => SampleEvidenceConcurrencyTests.LaterWrite(p, true, "detach"));
    [DisposablePostgresFact] public Task LaterWrite_false_sample() => Run(p => SampleEvidenceConcurrencyTests.LaterWrite(p, false, "sample"));
    [DisposablePostgresFact] public Task LaterWrite_false_detach() => Run(p => SampleEvidenceConcurrencyTests.LaterWrite(p, false, "detach"));
    [DisposablePostgresFact] public Task Lost() => Run(p => SampleEvidenceConcurrencyTests.Lost(p));
    [DisposablePostgresFact] public Task RollbackFresh_0() => Run(p => SampleEvidenceRollbackTests.Failure(p, "UPDATE \"SharedDrafts\"", false));
    [DisposablePostgresFact] public Task RollbackFresh_1() => Run(p => SampleEvidenceRollbackTests.Failure(p, "INSERT INTO \"AcceptedBatchReceipts\"", false));
    [DisposablePostgresFact] public Task RollbackFresh_2() => Run(p => SampleEvidenceRollbackTests.Failure(p, "INSERT INTO \"SampleEvidenceOperations\"", false));
    [DisposablePostgresFact] public Task RollbackFresh_3() => Run(p => SampleEvidenceRollbackTests.Failure(p, "INSERT INTO \"SampleEvidenceProjections\"", false));
    [DisposablePostgresFact] public Task RollbackFresh_4() => Run(p => SampleEvidenceRollbackTests.Failure(p, "INSERT INTO \"SharedAcceptanceAudits\"", false));
    [DisposablePostgresFact] public Task RollbackFresh_5() => Run(p => SampleEvidenceRollbackTests.Failure(p, "commit", false));
    [DisposablePostgresFact] public Task RollbackWithdraw_0() => Run(p => SampleEvidenceRollbackTests.Failure(p, "UPDATE \"SampleEvidenceProjections\"", true));
    [DisposablePostgresFact] public Task RollbackWithdraw_1() => Run(p => SampleEvidenceRollbackTests.Failure(p, "INSERT INTO \"SharedAcceptanceAudits\"", true));
    [DisposablePostgresFact] public Task RollbackWithdraw_2() => Run(p => SampleEvidenceRollbackTests.Failure(p, "commit", true));
    [DisposablePostgresFact] public Task NativeIntegrity() => Run(p => SampleEvidenceMigrationTests.Integrity(p));
    [DisposablePostgresFact] public async Task PopulatedUpgrade() { await using var p = await PhotoFixture.Create(true, SampleEvidenceMigrationTests.Previous); await SampleEvidenceMigrationTests.Upgrade(p); }
    [DisposablePostgresFact] public async Task MigrationRollback_0() { await using var p = await PhotoFixture.Create(true, SampleEvidenceMigrationTests.Previous); await SampleEvidenceMigrationTests.Rollback(p, "CREATE TABLE \"SampleEvidenceProjections\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_1() { await using var p = await PhotoFixture.Create(true, SampleEvidenceMigrationTests.Previous); await SampleEvidenceMigrationTests.Rollback(p, "CREATE TRIGGER \"TR_binding_scope\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_2() { await using var p = await PhotoFixture.Create(true, SampleEvidenceMigrationTests.Previous); await SampleEvidenceMigrationTests.Rollback(p, "INSERT INTO \"__EFMigrationsHistory\""); }
}
