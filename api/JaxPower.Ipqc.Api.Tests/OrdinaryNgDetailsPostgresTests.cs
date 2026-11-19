using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class OrdinaryNgDetailsPostgresTests
{
    private static async Task Run(Func<PersonalSessionFixture, Task> test)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(true);
        await test(f);
    }

    private static async Task Photo(Func<PhotoFixture, Task> test, string? migration = null)
    {
        await using var p = await PhotoFixture.Create(true, migration: migration);
        await test(p);
    }

    [DisposablePostgresFact] public Task Qualitative() => Run(f => OrdinaryNgDetailsTests.Lifecycle(f, false));
    [DisposablePostgresFact] public Task Quantitative() => Run(f => OrdinaryNgDetailsTests.Lifecycle(f, true));
    [DisposablePostgresFact] public Task BasisChanges() => Run(OrdinaryNgDetailsTests.BasisChanges);
    [DisposablePostgresFact] public Task Unable() => Photo(OrdinaryNgDetailsTests.Unable);
    [DisposablePostgresFact] public Task Shape() => Run(OrdinaryNgDetailsTests.Shape);
    [DisposablePostgresFact] public Task Authority() => Run(OrdinaryNgDetailsTests.Authority);
    [DisposablePostgresFact] public Task Namespace() => Photo(OrdinaryNgDetailsNamespaceTests.Namespace);
    [DisposablePostgresFact] public Task Upgrade() => Photo(OrdinaryNgDetailsIntegrityTests.Upgrade, OrdinaryNgDetailsFixture.PriorMigration);
    [DisposablePostgresFact] public Task Immutable() => Run(OrdinaryNgDetailsIntegrityTests.Immutable);
    [DisposablePostgresFact] public Task TamperActor() => Run(f => OrdinaryNgDetailsIntegrityTests.Tamper(f, "actor"));
    [DisposablePostgresFact] public Task TamperBasis() => Run(f => OrdinaryNgDetailsIntegrityTests.Tamper(f, "basis"));
    [DisposablePostgresFact] public Task RollbackOperation() => Run(f => OrdinaryNgDetailsIntegrityTests.Rollback(f, "operation"));
    [DisposablePostgresFact] public Task RollbackAudit() => Run(f => OrdinaryNgDetailsIntegrityTests.Rollback(f, "audit"));
    [DisposablePostgresFact] public Task RollbackCommit() => Run(f => OrdinaryNgDetailsIntegrityTests.Rollback(f, "commit"));
    [DisposablePostgresFact] public Task CasDetails() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "details", true);
    [DisposablePostgresFact] public Task CasDetailsBeforeSample() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "sample", true);
    [DisposablePostgresFact] public Task CasSampleBeforeDetails() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "sample", false);
    [DisposablePostgresFact] public Task CasDetailsBeforeRemoved() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "removed", true);
    [DisposablePostgresFact] public Task CasRemovedBeforeDetails() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "removed", false);
    [DisposablePostgresFact] public Task CasDetailsBeforePic() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "pic", true);
    [DisposablePostgresFact] public Task CasPicBeforeDetails() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "pic", false);
    [DisposablePostgresFact] public Task CasDetailsBeforeHeader() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "header", true);
    [DisposablePostgresFact] public Task CasHeaderBeforeDetails() => OrdinaryNgDetailsConcurrencyTests.Cas(true, "header", false);
}
