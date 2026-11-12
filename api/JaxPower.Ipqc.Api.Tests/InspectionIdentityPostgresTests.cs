namespace JaxPower.Ipqc.Api.Tests;

public sealed class InspectionIdentityPostgresTests
{
    [DisposablePostgresFact] public async Task ID02_Generation_commit_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionIdentityTests.GenerationCommitRollback(f); }
    [DisposablePostgresFact] public async Task ID03_Line_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.LineRace(f,true); }
    [DisposablePostgresFact] public async Task ID03_Initialization_before_line() { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.LineRace(f,false); }
    [DisposablePostgresFact] public async Task ID03_Logout_first() { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.LogoutRace(f,true); }
    [DisposablePostgresFact] public async Task ID03_Initialization_before_logout() { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.LogoutRace(f,false); }
    [DisposablePostgresFact] public async Task ID04_Upgrade_table_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true,migration: InspectionIdentityMigrationTests.Previous); await InspectionIdentityMigrationTests.UpgradeRollback(f,"table"); }
    [DisposablePostgresFact] public async Task ID04_Upgrade_history_rollback() { await using var f = await PersonalSessionFixture.CreateAsync(true,migration: InspectionIdentityMigrationTests.Previous); await InspectionIdentityMigrationTests.UpgradeRollback(f,"history"); }
    [DisposablePostgresFact] public async Task ID03_Explicit_initialization_concurrent_replay()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.InitializeRace(f); }
    [DisposablePostgresFact] public async Task ID03_Initialization_attempt_rollback()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.Rollback(f,"attempt"); }
    [DisposablePostgresFact] public async Task ID03_Initialization_applicability_rollback()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.Rollback(f,"applicability"); }
    [DisposablePostgresFact] public async Task ID03_Initialization_commit_rollback()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.Rollback(f,"commit"); }
    [DisposablePostgresFact] public async Task ID03_Initialization_authority()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.Authority(f); }
    [DisposablePostgresFact] public async Task ID03_Dirty_task_initialization_denied()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionInitializationTests.Unsupported(f,"answers"); }
    [DisposablePostgresFact] public async Task ID04_Database_scope_uniqueness_and_restrict()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionIdentityTests.Constraints(f); }
    [DisposablePostgresFact] public async Task ID01_Identity_and_WorkContext_compatibility()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionIdentityTests.Identity(f); }
    [DisposablePostgresFact] public async Task ID02_Concurrent_generation()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionIdentityTests.Generation(f); }
    [DisposablePostgresFact] public async Task ID02_Attempt_save_rollback()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionIdentityTests.GenerationRollback(f, "INSERT INTO \"InspectionAttempts\""); }
    [DisposablePostgresFact] public async Task ID02_Applicability_save_rollback()
    { await using var f = await PersonalSessionFixture.CreateAsync(true); await InspectionIdentityTests.GenerationRollback(f, "INSERT INTO \"InspectionAttemptApplicabilities\""); }
    [DisposablePostgresFact] public async Task ID04_Populated_upgrade_and_no_implicit_initialization()
    { await using var f = await PersonalSessionFixture.CreateAsync(true, migration: InspectionIdentityMigrationTests.Previous); await InspectionIdentityMigrationTests.Upgrade(f); }
}
