using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class BasicInformationPostgresTests
{
    private static async Task Run(Func<PersonalSessionFixture, Task> test) { await using var f = await PersonalSessionFixture.CreateAsync(true); await test(f); }
    private static async Task Photo(Func<PhotoFixture, Task> test) { await using var p = await PhotoFixture.Create(true); await test(p); }
    [DisposablePostgresFact] public Task Lifecycle() => Run(BasicInformationTests.Lifecycle);
    [DisposablePostgresFact] public Task Value_0() => Run(f => BasicInformationTests.Values(f, "null"));
    [DisposablePostgresFact] public Task Value_1() => Run(f => BasicInformationTests.Values(f, "empty"));
    [DisposablePostgresFact] public Task Value_2() => Run(f => BasicInformationTests.Values(f, "space"));
    [DisposablePostgresFact] public Task Value_3() => Run(f => BasicInformationTests.Values(f, "max"));
    [DisposablePostgresFact] public Task Value_4() => Run(f => BasicInformationTests.Values(f, "unicode"));
    [DisposablePostgresFact] public Task InvalidValue_0() => Run(f => BasicInformationTests.InvalidValue(f, "long"));
    [DisposablePostgresFact] public Task InvalidValue_1() => Run(f => BasicInformationTests.InvalidValue(f, "unicode-long"));
    [DisposablePostgresFact] public Task InvalidValue_2() => Run(f => BasicInformationTests.InvalidValue(f, "control"));
    [DisposablePostgresFact] public Task InvalidValue_3() => Run(f => BasicInformationTests.InvalidValue(f, "newline"));
    [DisposablePostgresFact] public Task Replay() => Run(BasicInformationTests.Replay);
    [DisposablePostgresFact] public Task LostResponse() => Run(BasicInformationTests.LostResponse);
    [DisposablePostgresFact] public Task Predecessor() => Run(BasicInformationTests.Predecessor);
    [DisposablePostgresFact] public Task Paging() => Run(BasicInformationTests.Paging);
    [DisposablePostgresFact] public Task Authority() => Run(BasicInformationSecurityTests.Authority);
    [DisposablePostgresFact] public Task Shape() => Run(BasicInformationSecurityTests.Shape);
    [DisposablePostgresFact] public Task Scope() => Run(BasicInformationSecurityTests.Scope);
    [DisposablePostgresFact] public Task InvalidSnapshot() => Run(f => BasicInformationSecurityTests.Unavailable(f, "snapshot"));
    [DisposablePostgresFact] public Task MissingCurrentAttempt() => Run(f => BasicInformationSecurityTests.Unavailable(f, "attempt"));
    [DisposablePostgresFact] public Task Normal_0() => Run(f => BasicInformationSecurityTests.Normal(f, "logout"));
    [DisposablePostgresFact] public Task Normal_1() => Run(f => BasicInformationSecurityTests.Normal(f, "self-password"));
    [DisposablePostgresFact] public Task Normal_2() => Run(f => BasicInformationSecurityTests.Normal(f, "expiry"));
    [DisposablePostgresFact] public Task Normal_3() => Run(f => BasicInformationSecurityTests.Normal(f, "refresh"));
    [DisposablePostgresFact] public Task Security_0() => Run(f => BasicInformationSecurityTests.Security(f, "reset"));
    [DisposablePostgresFact] public Task Security_1() => Run(f => BasicInformationSecurityTests.Security(f, "role"));
    [DisposablePostgresFact] public Task Security_2() => Run(f => BasicInformationSecurityTests.Security(f, "deactivate"));
    [DisposablePostgresFact] public Task Security_3() => Run(f => BasicInformationSecurityTests.Security(f, "explicit"));
    [DisposablePostgresFact] public Task AcceptanceRollback_0() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "UPDATE \"SharedDrafts\""));
    [DisposablePostgresFact] public Task AcceptanceRollback_1() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "INSERT INTO \"AcceptedBatchReceipts\""));
    [DisposablePostgresFact] public Task AcceptanceRollback_2() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "INSERT INTO \"TaskHeaderOperations\""));
    [DisposablePostgresFact] public Task AcceptanceRollback_3() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "INSERT INTO \"TaskHeaderProjections\""));
    [DisposablePostgresFact] public Task AcceptanceRollback_4() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "UPDATE \"TaskHeaderProjections\""));
    [DisposablePostgresFact] public Task AcceptanceRollback_5() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task AcceptanceRollback_6() => Run(f => BasicInformationMigrationTests.AcceptanceRollback(f, "commit"));
    [DisposablePostgresFact] public Task Integrity() => Run(BasicInformationMigrationTests.Integrity);
    [DisposablePostgresFact] public Task ReadRace_0() => Run(f => BasicInformationConcurrencyTests.ReadRace(f, true));
    [DisposablePostgresFact] public Task ReadRace_1() => Run(f => BasicInformationConcurrencyTests.ReadRace(f, false));
    [DisposablePostgresFact] public Task SecurityRace_0_0() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, true, "reset"));
    [DisposablePostgresFact] public Task SecurityRace_0_1() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, true, "role"));
    [DisposablePostgresFact] public Task SecurityRace_0_2() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, true, "deactivate"));
    [DisposablePostgresFact] public Task SecurityRace_0_3() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, true, "explicit"));
    [DisposablePostgresFact] public Task SecurityRace_1_0() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, false, "reset"));
    [DisposablePostgresFact] public Task SecurityRace_1_1() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, false, "role"));
    [DisposablePostgresFact] public Task SecurityRace_1_2() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, false, "deactivate"));
    [DisposablePostgresFact] public Task SecurityRace_1_3() => Run(f => BasicInformationConcurrencyTests.SecurityRace(f, false, "explicit"));
    [DisposablePostgresFact] public Task LogoutRace_0() => Run(f => BasicInformationConcurrencyTests.LogoutRace(f, true));
    [DisposablePostgresFact] public Task LogoutRace_1() => Run(f => BasicInformationConcurrencyTests.LogoutRace(f, false));
    [DisposablePostgresFact] public Task Upgrade() => Photo(BasicInformationMigrationTests.Upgrade);
    [DisposablePostgresFact] public Task MigrationRollback_0() => Photo(p => BasicInformationMigrationTests.MigrationRollback(p, "CREATE TABLE \"TaskHeaderProjections\""));
    [DisposablePostgresFact] public Task MigrationRollback_1() => Photo(p => BasicInformationMigrationTests.MigrationRollback(p, "CREATE TRIGGER \"TR_header_scope\""));
    [DisposablePostgresFact] public Task MigrationRollback_2() => Photo(p => BasicInformationMigrationTests.MigrationRollback(p, "INSERT INTO \"__EFMigrationsHistory\""));
    [DisposablePostgresFact] public Task Namespace() => Photo(BasicInformationIdentityTests.Namespace);
    [DisposablePostgresFact] public Task Cas_0_0() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "sample"));
    [DisposablePostgresFact] public Task Cas_0_1() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "removed"));
    [DisposablePostgresFact] public Task Cas_0_2() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "unable"));
    [DisposablePostgresFact] public Task Cas_0_3() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "attachment"));
    [DisposablePostgresFact] public Task Cas_0_4() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "binding"));
    [DisposablePostgresFact] public Task Cas_0_5() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "header-same"));
    [DisposablePostgresFact] public Task Cas_0_6() => Photo(p => BasicInformationConcurrencyTests.Cas(p, true, "header-other"));
    [DisposablePostgresFact] public Task Cas_1_0() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "sample"));
    [DisposablePostgresFact] public Task Cas_1_1() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "removed"));
    [DisposablePostgresFact] public Task Cas_1_2() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "unable"));
    [DisposablePostgresFact] public Task Cas_1_3() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "attachment"));
    [DisposablePostgresFact] public Task Cas_1_4() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "binding"));
    [DisposablePostgresFact] public Task Cas_1_5() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "header-same"));
    [DisposablePostgresFact] public Task Cas_1_6() => Photo(p => BasicInformationConcurrencyTests.Cas(p, false, "header-other"));
}
