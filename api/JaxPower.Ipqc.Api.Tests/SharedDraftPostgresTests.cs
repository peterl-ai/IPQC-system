namespace JaxPower.Ipqc.Api.Tests;

public sealed class SharedDraftPostgresTests
{
    [DisposablePostgresFact] public Task Frozen_request_manifest()=>Run(SharedDraftConcurrencyTests.FrozenRequest);
    [DisposablePostgresFact] public Task Multi_source_account_order()=>Run(SharedDraftConcurrencyTests.MultiSource);
    [DisposablePostgresFact] public Task Line_first()=>Run(f=>SharedDraftConcurrencyTests.LineRace(f,true));
    [DisposablePostgresFact] public Task Acceptance_before_line_deactivation()=>Run(f=>SharedDraftConcurrencyTests.LineRace(f,false));
    private static async Task Run(Func<PersonalSessionFixture,Task> test) {await using var f=await PersonalSessionFixture.CreateAsync(true);await test(f);}
    [DisposablePostgresFact] public Task Lifecycle()=>Run(SharedDraftTests.Lifecycle);
    [DisposablePostgresFact] public Task Frozen_quantitative()=>Run(f=>SharedDraftTests.Quantitative(f,2.51m,"NG"));
    [DisposablePostgresFact] public Task Cas()=>Run(SharedDraftConcurrencyTests.Cas);
    [DisposablePostgresFact] public Task Lost_response()=>Run(SharedDraftConcurrencyTests.LostResponse);
    [DisposablePostgresFact] public Task Revoke_first()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,true,"explicit"));
    [DisposablePostgresFact] public Task Acceptance_before_revoke()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,false,"explicit"));
    [DisposablePostgresFact] public Task Deactivation_first()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,true,"deactivate"));
    [DisposablePostgresFact] public Task Acceptance_before_deactivation()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,false,"deactivate"));
    [DisposablePostgresFact] public Task Role_loss_first()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,true,"role"));
    [DisposablePostgresFact] public Task Acceptance_before_role_loss()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,false,"role"));
    [DisposablePostgresFact] public Task Reset_first()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,true,"reset"));
    [DisposablePostgresFact] public Task Acceptance_before_reset()=>Run(f=>SharedDraftConcurrencyTests.SecurityRace(f,false,"reset"));
    [DisposablePostgresFact] public Task Source_logout()=>Run(f=>SharedDraftSecurityTests.Normal(f,"logout"));
    [DisposablePostgresFact] public Task Source_expiry()=>Run(f=>SharedDraftSecurityTests.Normal(f,"expiry"));
    [DisposablePostgresFact] public Task Source_password()=>Run(f=>SharedDraftSecurityTests.Normal(f,"self-password"));
    [DisposablePostgresFact] public Task Source_refresh()=>Run(f=>SharedDraftSecurityTests.Normal(f,"refresh"));
    [DisposablePostgresFact] public Task Mixed_bad_proof()=>Run(f=>SharedDraftSecurityTests.Mixed(f,"proof"));
    [DisposablePostgresFact] public Task Mixed_wrong_owner()=>Run(f=>SharedDraftSecurityTests.Mixed(f,"owner"));
    [DisposablePostgresFact] public Task Mixed_missing_proof()=>Run(f=>SharedDraftSecurityTests.Mixed(f,"missing"));
    [DisposablePostgresFact] public Task Mixed_unknown_source()=>Run(f=>SharedDraftSecurityTests.Mixed(f,"unknown"));
    [DisposablePostgresFact] public Task Accepted_refs_after_revoke()=>Run(f=>SharedDraftSecurityTests.Security(f,"explicit"));
    [DisposablePostgresFact] public Task Accepted_refs_after_reset()=>Run(f=>SharedDraftSecurityTests.Security(f,"reset"));
    [DisposablePostgresFact] public Task Reactivation_does_not_revive_sources()=>Run(f=>SharedDraftSecurityTests.Security(f,"deactivate"));
    [DisposablePostgresFact] public Task Role_restoration_does_not_revive_sources()=>Run(f=>SharedDraftSecurityTests.Security(f,"role"));
    [DisposablePostgresFact] public Task Authority_and_historical_recovery()=>Run(SharedDraftSecurityTests.Authority);
    [DisposablePostgresFact] public Task State_first()=>Run(f=>SharedDraftConcurrencyTests.StateRace(f,true));
    [DisposablePostgresFact] public Task Acceptance_before_state()=>Run(f=>SharedDraftConcurrencyTests.StateRace(f,false));
    [DisposablePostgresFact] public Task B_logout_first()=>Run(f=>SharedDraftConcurrencyTests.FamilyRace(f,true,"logout"));
    [DisposablePostgresFact] public Task Acceptance_before_B_logout()=>Run(f=>SharedDraftConcurrencyTests.FamilyRace(f,false,"logout"));
    [DisposablePostgresFact] public Task B_refresh_first()=>Run(f=>SharedDraftConcurrencyTests.FamilyRace(f,true,"refresh"));
    [DisposablePostgresFact] public Task Acceptance_before_B_refresh()=>Run(f=>SharedDraftConcurrencyTests.FamilyRace(f,false,"refresh"));
    [DisposablePostgresFact] public async Task Populated_upgrade() {await using var f=await PersonalSessionFixture.CreateAsync(true,migration:SharedDraftMigrationTests.Previous);await SharedDraftMigrationTests.Upgrade(f);}
    [DisposablePostgresFact] public async Task Upgrade_table_rollback() {await using var f=await PersonalSessionFixture.CreateAsync(true,migration:SharedDraftMigrationTests.Previous);await SharedDraftMigrationTests.UpgradeRollback(f,"CREATE TABLE \"SharedSamples\"");}
    [DisposablePostgresFact] public async Task Upgrade_history_rollback() {await using var f=await PersonalSessionFixture.CreateAsync(true,migration:SharedDraftMigrationTests.Previous);await SharedDraftMigrationTests.UpgradeRollback(f,"INSERT INTO \"__EFMigrationsHistory\"");}
    [DisposablePostgresFact] public Task Native_scope()=>Run(SharedDraftIntegrityTests.Scope);
    [DisposablePostgresFact] public Task Immutable_history()=>Run(SharedDraftIntegrityTests.Immutable);
    [DisposablePostgresFact] public Task Root_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"INSERT INTO \"SharedDrafts\""));
    [DisposablePostgresFact] public Task Cas_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"UPDATE \"SharedDrafts\""));
    [DisposablePostgresFact] public Task Sample_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"INSERT INTO \"SharedSamples\""));
    [DisposablePostgresFact] public Task Batch_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"INSERT INTO \"AcceptedBatchReceipts\""));
    [DisposablePostgresFact] public Task Ledger_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"INSERT INTO \"ContributionOperations\""));
    [DisposablePostgresFact] public Task Projection_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"INSERT INTO \"SharedSampleProjections\""));
    [DisposablePostgresFact] public Task Audit_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task Commit_rollback()=>Run(f=>SharedDraftRollbackTests.Fresh(f,"commit"));
    [DisposablePostgresFact] public Task Dependency_rollback()=>Run(f=>SharedDraftRollbackTests.Correction(f,"INSERT INTO \"ContributionDependencies\""));
    [DisposablePostgresFact] public Task Corrected_projection_rollback()=>Run(f=>SharedDraftRollbackTests.Correction(f,"UPDATE \"SharedSampleProjections\""));
    [DisposablePostgresFact] public Task Correction_audit_rollback()=>Run(f=>SharedDraftRollbackTests.Correction(f,"INSERT INTO \"SharedAcceptanceAudits\""));
    [DisposablePostgresFact] public Task Correction_commit_rollback()=>Run(f=>SharedDraftRollbackTests.Correction(f,"commit"));
    [DisposablePostgresFact] public Task Redaction()=>Run(SharedDraftCanonicalTests.Redaction);
    [DisposablePostgresFact] public Task Cyclic_manifest()=>Run(f=>SharedDraftManifestTests.Negative(f,"cycle"));
    [DisposablePostgresFact] public Task Missing_dependency()=>Run(f=>SharedDraftManifestTests.Negative(f,"missing"));
    [DisposablePostgresFact] public Task Foreign_attempt()=>Run(f=>SharedDraftManifestTests.Negative(f,"foreign-attempt"));
    [DisposablePostgresFact] public Task Wrong_dependency_hash()=>Run(f=>SharedDraftManifestTests.Negative(f,"wrong-hash"));
    [DisposablePostgresFact] public Task Duplicate_client_capture()=>Run(f=>SharedDraftManifestTests.Negative(f,"duplicate-client"));
}
