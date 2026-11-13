namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoPostgresTests
{
    private static async Task Run(Func<PhotoFixture,Task> action) {await using var p=await PhotoFixture.Create(true);await action(p);}
    [DisposablePostgresFact] public Task Png_bytes_and_replay()=>Run(p=>PhotoTests.Lifecycle(p));
    [DisposablePostgresFact] public Task Jpeg_bytes_and_replay()=>Run(p=>PhotoTests.Lifecycle(p,true));
    [DisposablePostgresFact] public Task Authority()=>Run(PhotoTests.Authority);
    [DisposablePostgresFact] public Task Actual_attempt_scope_and_removed()=>Run(PhotoTests.Scope);
    [DisposablePostgresFact] public Task Pending_photo_slot_limit()=>Run(PhotoTests.Slots);
    [DisposablePostgresFact] public Task Byte_identity_conflict()=>Run(p=>PhotoTests.Conflict(p,"bytes"));
    [DisposablePostgresFact] public Task Source_identity_conflict()=>Run(p=>PhotoTests.Conflict(p,"source"));
    [DisposablePostgresFact] public Task Item_identity_conflict()=>Run(p=>PhotoTests.Conflict(p,"item"));
    [DisposablePostgresFact] public Task Attempt_identity_conflict()=>Run(p=>PhotoTests.Conflict(p,"attempt"));
    [DisposablePostgresFact] public Task Task_identity_conflict()=>Run(p=>PhotoTests.Conflict(p,"task"));
    [DisposablePostgresFact] public Task Hash_mismatch()=>Run(p=>PhotoTests.Invalid(p,"hash"));
    [DisposablePostgresFact] public Task Size_mismatch()=>Run(p=>PhotoTests.Invalid(p,"size"));
    [DisposablePostgresFact] public Task Mime_mismatch()=>Run(p=>PhotoTests.Invalid(p,"mime"));
    [DisposablePostgresFact] public Task Corrupt_input()=>Run(p=>PhotoTests.Invalid(p,"corrupt"));
    [DisposablePostgresFact] public Task Truncated_input()=>Run(p=>PhotoTests.Invalid(p,"truncated"));
    [DisposablePostgresFact] public Task Oversized_input()=>Run(p=>PhotoTests.Invalid(p,"oversize"));
    [DisposablePostgresFact] public Task Dimension_limit()=>Run(p=>PhotoTests.Invalid(p,"bomb"));
    [DisposablePostgresFact] public Task Normal_logout()=>Run(p=>PhotoTests.NormalA(p,"logout"));
    [DisposablePostgresFact] public Task Normal_expiry()=>Run(p=>PhotoTests.NormalA(p,"expiry"));
    [DisposablePostgresFact] public Task Normal_password_change()=>Run(p=>PhotoTests.NormalA(p,"self-password"));
    [DisposablePostgresFact] public Task Missing_bytes()=>Run(p=>PhotoTests.Loss(p,false));
    [DisposablePostgresFact] public Task Corrupt_bytes()=>Run(p=>PhotoTests.Loss(p,true));
    [DisposablePostgresFact] public Task Disk_failure()=>Run(p=>PhotoRecoveryTests.Failure(p,"disk"));
    [DisposablePostgresFact] public Task Interrupted_stage()=>Run(p=>PhotoRecoveryTests.Failure(p,"stream"));
    [DisposablePostgresFact] public Task Stored_metadata_rollback()=>Run(p=>PhotoRecoveryTests.Failure(p,"stored"));
    [DisposablePostgresFact] public Task Before_rename_failure()=>Run(p=>PhotoRecoveryTests.Failure(p,"rename"));
    [DisposablePostgresFact] public Task After_rename_failure()=>Run(p=>PhotoRecoveryTests.Failure(p,"after-rename"));
    [DisposablePostgresFact] public Task Ready_commit_rollback()=>Run(p=>PhotoRecoveryTests.Failure(p,"commit"));
    [DisposablePostgresFact] public Task Ready_audit_rollback()=>Run(p=>PhotoRecoveryTests.Failure(p,"audit"));
    [DisposablePostgresFact] public Task Crash_receiving_restart()=>Run(p=>PhotoRecoveryTests.Failure(p,"crash-receiving"));
    [DisposablePostgresFact] public Task Lost_ready_response()=>Run(PhotoRecoveryTests.LostResponse);
    [DisposablePostgresFact] public Task Revoke_before_ready()=>Run(p=>PhotoConcurrencyTests.Security(p,true,"explicit"));
    [DisposablePostgresFact] public Task Ready_before_revoke()=>Run(p=>PhotoConcurrencyTests.Security(p,false,"explicit"));
    [DisposablePostgresFact] public Task Reset_before_ready()=>Run(p=>PhotoConcurrencyTests.Security(p,true,"reset"));
    [DisposablePostgresFact] public Task Ready_before_reset()=>Run(p=>PhotoConcurrencyTests.Security(p,false,"reset"));
    [DisposablePostgresFact] public Task Role_loss_before_ready()=>Run(p=>PhotoConcurrencyTests.Security(p,true,"role"));
    [DisposablePostgresFact] public Task Ready_before_role_loss()=>Run(p=>PhotoConcurrencyTests.Security(p,false,"role"));
    [DisposablePostgresFact] public Task Deactivation_before_ready()=>Run(p=>PhotoConcurrencyTests.Security(p,true,"deactivate"));
    [DisposablePostgresFact] public Task Ready_before_deactivation()=>Run(p=>PhotoConcurrencyTests.Security(p,false,"deactivate"));
    [DisposablePostgresFact] public Task Lease_fencing()=>Run(PhotoConcurrencyTests.Lease);
    [DisposablePostgresFact] public Task Cross_task_identity_unique_race()=>Run(PhotoConcurrencyTests.ClientIdentity);
    [DisposablePostgresFact] public Task Expired_unclaimed_lease()=>Run(PhotoConcurrencyTests.Expiry);
    [DisposablePostgresFact] public Task Uploader_revoked_after_stream()=>Run(PhotoConcurrencyTests.BRevoked);
    [DisposablePostgresFact] public Task Native_integrity()=>Run(PhotoMigrationTests.Integrity);
    [DisposablePostgresFact] public async Task Populated_upgrade() {await using var p=await PhotoFixture.Create(true,PhotoMigrationTests.Previous);await PhotoMigrationTests.Upgrade(p);}
    [DisposablePostgresFact] public async Task Table_upgrade_rollback() {await using var p=await PhotoFixture.Create(true,PhotoMigrationTests.Previous);await PhotoMigrationTests.Rollback(p,"CREATE TABLE \"AttachmentUploads\"");}
    [DisposablePostgresFact] public async Task Trigger_upgrade_rollback() {await using var p=await PhotoFixture.Create(true,PhotoMigrationTests.Previous);await PhotoMigrationTests.Rollback(p,"CREATE TRIGGER \"TR_PhotoScope\"");}
    [DisposablePostgresFact] public async Task History_upgrade_rollback() {await using var p=await PhotoFixture.Create(true,PhotoMigrationTests.Previous);await PhotoMigrationTests.Rollback(p,"INSERT INTO \"__EFMigrationsHistory\"");}
    [DisposablePostgresFact] public async Task Full_environment_width() {await using var p=await PhotoFixture.Create(true,environment:new string('e',200));await PhotoTests.Lifecycle(p);}
}
