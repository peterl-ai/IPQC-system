using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoPolicyPostgresTests
{
    [DisposablePostgresFact] public async Task Lifecycle() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyTests.Lifecycle(f); }
    [DisposablePostgresFact] public async Task Drift() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyTests.Drift(f); }
    [DisposablePostgresFact] public async Task Legacy() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Legacy(p); }
    [DisposablePostgresFact] public async Task Evidence_0() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Required", "none"); }
    [DisposablePostgresFact] public async Task Evidence_1() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Required", "unassociated"); }
    [DisposablePostgresFact] public async Task Evidence_2() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Required", "valid"); }
    [DisposablePostgresFact] public async Task Evidence_3() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Required", "missing"); }
    [DisposablePostgresFact] public async Task Evidence_4() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Required", "corrupt"); }
    [DisposablePostgresFact] public async Task Evidence_5() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Required", "detached"); }
    [DisposablePostgresFact] public async Task Evidence_6() { await using var p = await PhotoFixture.Create(true); await PhotoPolicyTests.Evidence(p, "Optional", "none"); }
    [DisposablePostgresFact] public async Task InvalidValue_0() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Value(f, "required"); }
    [DisposablePostgresFact] public async Task InvalidValue_1() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Value(f, "Unknown"); }
    [DisposablePostgresFact] public async Task InvalidValue_2() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Value(f, ""); }
    [DisposablePostgresFact] public async Task InvalidValue_3() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Value(f, " Required "); }
    [DisposablePostgresFact] public async Task Authority() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Authority(f); }
    [DisposablePostgresFact] public async Task Scope() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Scope(f); }
    [DisposablePostgresFact] public async Task Minimum() { await using var p = await PhotoFixture.Create(true); await PhotoPolicySecurityTests.Minimum(p); }
    [DisposablePostgresFact] public async Task ConfirmationRollback_0() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Rollback(f, "INSERT INTO \"PhotoPolicyDefinitions\""); }
    [DisposablePostgresFact] public async Task ConfirmationRollback_1() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Rollback(f, "INSERT INTO \"PhotoPolicyStates\""); }
    [DisposablePostgresFact] public async Task ConfirmationRollback_2() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Rollback(f, "INSERT INTO \"PhotoPolicyConfirmations\""); }
    [DisposablePostgresFact] public async Task ConfirmationRollback_3() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Rollback(f, "UPDATE \"PhotoPolicyStates\""); }
    [DisposablePostgresFact] public async Task ConfirmationRollback_4() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.Rollback(f, "commit"); }
    [DisposablePostgresFact] public async Task GenerationRollback_0() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.GenerationRollback(f, "INSERT INTO \"PatrolTasks\""); }
    [DisposablePostgresFact] public async Task GenerationRollback_1() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.GenerationRollback(f, "INSERT INTO \"PatrolTaskItems\""); }
    [DisposablePostgresFact] public async Task GenerationRollback_2() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.GenerationRollback(f, "INSERT INTO \"FrozenTaskPhotoPolicies\""); }
    [DisposablePostgresFact] public async Task GenerationRollback_3() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicySecurityTests.GenerationRollback(f, "commit"); }
    [DisposablePostgresFact] public async Task Definition_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Definition(f, true); }
    [DisposablePostgresFact] public async Task Definition_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Definition(f, false); }
    [DisposablePostgresFact] public async Task Generation_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Generation(f, true); }
    [DisposablePostgresFact] public async Task Generation_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Generation(f, false); }
    [DisposablePostgresFact] public async Task Plan_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Plan(f, true); }
    [DisposablePostgresFact] public async Task Plan_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Plan(f, false); }
    [DisposablePostgresFact] public async Task Security_reset_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, true, "reset"); }
    [DisposablePostgresFact] public async Task Security_reset_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, false, "reset"); }
    [DisposablePostgresFact] public async Task Security_role_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, true, "role"); }
    [DisposablePostgresFact] public async Task Security_role_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, false, "role"); }
    [DisposablePostgresFact] public async Task Security_deactivate_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, true, "deactivate"); }
    [DisposablePostgresFact] public async Task Security_deactivate_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, false, "deactivate"); }
    [DisposablePostgresFact] public async Task Security_logout_true() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, true, "logout"); }
    [DisposablePostgresFact] public async Task Security_logout_false() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Security(f, false, "logout"); }
    [DisposablePostgresFact] public async Task Duplicate() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyConcurrencyTests.Duplicate(f); }
    [DisposablePostgresFact] public async Task Integrity() { await using var f = await PersonalSessionFixture.CreateAsync(true); await PhotoPolicyMigrationTests.Integrity(f); }
    [DisposablePostgresFact] public async Task PopulatedUpgrade() { await using var p = await PhotoFixture.Create(true, PhotoPolicyFixture.Previous); await PhotoPolicyMigrationTests.Upgrade(p); }
    [DisposablePostgresFact] public async Task MigrationRollback_0() { await using var p = await PhotoFixture.Create(true, PhotoPolicyFixture.Previous); await PhotoPolicyMigrationTests.Rollback(p, "CREATE TABLE \"PhotoPolicyStates\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_1() { await using var p = await PhotoFixture.Create(true, PhotoPolicyFixture.Previous); await PhotoPolicyMigrationTests.Rollback(p, "CREATE TRIGGER \"TR_policy_confirmation_scope\""); }
    [DisposablePostgresFact] public async Task MigrationRollback_2() { await using var p = await PhotoFixture.Create(true, PhotoPolicyFixture.Previous); await PhotoPolicyMigrationTests.Rollback(p, "INSERT INTO \"__EFMigrationsHistory\""); }
}
