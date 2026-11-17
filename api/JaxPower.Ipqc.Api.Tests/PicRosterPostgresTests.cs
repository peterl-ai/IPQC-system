using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PicRosterPostgresTests
{
    [DisposablePostgresFact] public Task ConcurrentCreate_0() => Run(f => PicRosterConcurrencyTests.CreateRace(f, false));
    [DisposablePostgresFact] public Task ConcurrentCreate_1() => Run(f => PicRosterConcurrencyTests.CreateRace(f, true));
    private static async Task Run(Func<PersonalSessionFixture, Task> test) { await using var f = await PersonalSessionFixture.CreateAsync(true); await test(f); }
    private static async Task Prior(Func<PhotoFixture, Task> test) { await using var p = await PhotoFixture.Create(true, migration: PicRosterMigrationTests.Previous); await test(p); }
    [DisposablePostgresFact] public Task Lifecycle() => Run(f => PicRosterTests.Lifecycle(f));
    [DisposablePostgresFact] public Task Scope() => Run(f => PicRosterTests.Scope(f));
    [DisposablePostgresFact] public Task Authority() => Run(f => PicRosterTests.Authority(f));
    [DisposablePostgresFact] public Task Invalid_0() => Run(f => PicRosterTests.Invalid(f, "null-name"));
    [DisposablePostgresFact] public Task Invalid_1() => Run(f => PicRosterTests.Invalid(f, "empty"));
    [DisposablePostgresFact] public Task Invalid_2() => Run(f => PicRosterTests.Invalid(f, "space"));
    [DisposablePostgresFact] public Task Invalid_3() => Run(f => PicRosterTests.Invalid(f, "long"));
    [DisposablePostgresFact] public Task Invalid_4() => Run(f => PicRosterTests.Invalid(f, "control"));
    [DisposablePostgresFact] public Task Invalid_5() => Run(f => PicRosterTests.Invalid(f, "null-dept"));
    [DisposablePostgresFact] public Task Invalid_6() => Run(f => PicRosterTests.Invalid(f, "long-dept"));
    [DisposablePostgresFact] public Task Invalid_7() => Run(f => PicRosterTests.Invalid(f, "unicode-long"));
    [DisposablePostgresFact] public Task Shape() => Run(f => PicRosterTests.Shape(f));
    [DisposablePostgresFact] public Task Replay() => Run(f => PicRosterTests.Replay(f));
    [DisposablePostgresFact] public Task Lost() => Run(f => PicRosterTests.Lost(f));
    [DisposablePostgresFact] public Task Paging() => Run(f => PicRosterTests.Paging(f));
    [DisposablePostgresFact] public Task Cas_0_0() => Run(f => PicRosterConcurrencyTests.Cas(f, true, "edit"));
    [DisposablePostgresFact] public Task Cas_0_1() => Run(f => PicRosterConcurrencyTests.Cas(f, false, "edit"));
    [DisposablePostgresFact] public Task Cas_1_0() => Run(f => PicRosterConcurrencyTests.Cas(f, true, "deactivate"));
    [DisposablePostgresFact] public Task Cas_1_1() => Run(f => PicRosterConcurrencyTests.Cas(f, false, "deactivate"));
    [DisposablePostgresFact] public Task Cas_2_0() => Run(f => PicRosterConcurrencyTests.Cas(f, true, "retry"));
    [DisposablePostgresFact] public Task Cas_2_1() => Run(f => PicRosterConcurrencyTests.Cas(f, false, "retry"));
    [DisposablePostgresFact] public Task Logout_0() => Run(f => PicRosterConcurrencyTests.Logout(f, true));
    [DisposablePostgresFact] public Task Logout_1() => Run(f => PicRosterConcurrencyTests.Logout(f, false));
    [DisposablePostgresFact] public Task Rollback_0() => Run(f => PicRosterMigrationTests.AcceptanceRollback(f, "entry", false));
    [DisposablePostgresFact] public Task Rollback_1() => Run(f => PicRosterMigrationTests.AcceptanceRollback(f, "revision", false));
    [DisposablePostgresFact] public Task Rollback_2() => Run(f => PicRosterMigrationTests.AcceptanceRollback(f, "commit", false));
    [DisposablePostgresFact] public Task Rollback_3() => Run(f => PicRosterMigrationTests.AcceptanceRollback(f, "revision", true));
    [DisposablePostgresFact] public Task Rollback_4() => Run(f => PicRosterMigrationTests.AcceptanceRollback(f, "commit", true));
    [DisposablePostgresFact] public Task Integrity() => Run(f => PicRosterMigrationTests.Integrity(f));
    [DisposablePostgresFact] public Task Upgrade() => Prior(PicRosterMigrationTests.Upgrade);
    [DisposablePostgresFact] public Task MigrationRollback_0() => Prior(p => PicRosterMigrationTests.MigrationRollback(p, "CREATE TABLE \"PicRosterRevisions\""));
    [DisposablePostgresFact] public Task MigrationRollback_1() => Prior(p => PicRosterMigrationTests.MigrationRollback(p, "CREATE TRIGGER \"TR_pic_revision\""));
    [DisposablePostgresFact] public Task MigrationRollback_2() => Prior(p => PicRosterMigrationTests.MigrationRollback(p, "INSERT INTO \"__EFMigrationsHistory\""));
}
