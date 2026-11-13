using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<DraftOperationKey> DraftOperationKeys => Set<DraftOperationKey>();
    public DbSet<DraftAttachmentOperation> DraftAttachmentOperations => Set<DraftAttachmentOperation>();
    public DbSet<DraftAttachmentProjection> DraftAttachmentProjections => Set<DraftAttachmentProjection>();

    private static void ConfigureDraftAttachments(ModelBuilder b)
    {
        var k = b.Entity<DraftOperationKey>(); k.HasKey(x => new { x.TaskId, x.OperationId });
        k.Property(x => x.Domain).HasMaxLength(20);
        k.HasOne<SharedDraft>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        k.ToTable(t => t.HasCheckConstraint("CK_DraftOperationKeys_Domain", "\"Domain\" IN ('Sample','ItemRemoval','AttachmentReference')"));
        var o = b.Entity<DraftAttachmentOperation>(); o.HasKey(x => new { x.TaskId, x.OperationId });
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.AttachmentId }).HasName("AK_DraftPhoto_OperationAttachment");
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.AttachmentId, x.ContentHash }).HasName("AK_DraftPhoto_PredecessorScope");
        o.HasIndex(x => new { x.TaskId, x.PredecessorOperationId, x.AttachmentId, x.PredecessorHash }).HasDatabaseName("IX_DraftPhoto_Predecessor");
        o.HasIndex(x => new { x.TaskId, x.DraftVersion, x.OperationId }).HasDatabaseName("IX_DraftPhoto_History");
        o.Property(x => x.EnvironmentId).HasMaxLength(200); o.Property(x => x.Kind).HasMaxLength(40);
        o.Property(x => x.ContentHash).HasMaxLength(64); o.Property(x => x.Sha256).HasMaxLength(64); o.Property(x => x.PredecessorHash).HasMaxLength(64);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId }).HasConstraintName("FK_DraftPhoto_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<AttachmentAcceptance>().WithMany().HasForeignKey(x => x.AttachmentId).HasConstraintName("FK_DraftPhoto_StorageAcceptance").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x => new { x.InspectionAttemptId, x.TaskItemId }).HasPrincipalKey(x => new { x.Id, x.PatrolTaskItemId }).HasConstraintName("FK_DraftPhoto_Attempt").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<DraftAttachmentOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.PredecessorOperationId, x.AttachmentId, x.PredecessorHash }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.AttachmentId, x.ContentHash }).HasConstraintName("FK_DraftPhoto_Predecessor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).HasConstraintName("FK_DraftPhoto_Source").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).HasConstraintName("FK_DraftPhoto_Contributor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_DraftPhoto_Actor").OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t => t.HasCheckConstraint("CK_DraftPhoto_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"Kind\" IN ('AssociateAttachment','DetachAttachment') AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"Sha256\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0 AND ((\"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL AND \"Kind\" = 'AssociateAttachment') OR (\"PredecessorOperationId\" IS NOT NULL AND length(\"PredecessorHash\") = 64))"));
        var p = b.Entity<DraftAttachmentProjection>(); p.HasKey(x => new { x.TaskId, x.AttachmentId });
        p.HasIndex(x => new { x.TaskId, x.LastOperationId, x.AttachmentId }).HasDatabaseName("IX_DraftPhotoProjection_Last");
        p.HasOne<DraftAttachmentOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.LastOperationId, x.AttachmentId }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.AttachmentId }).HasConstraintName("FK_DraftPhotoProjection_Operation").OnDelete(DeleteBehavior.Restrict);
    }
    private void GuardDraftAttachments()
    {
        foreach (var e in ChangeTracker.Entries()) {
            if (e.Entity is DraftOperationKey or DraftAttachmentOperation && e.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Draft attachment history is immutable.");
            if (e.Entity is DraftAttachmentProjection && (e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name is nameof(DraftAttachmentProjection.TaskId) or nameof(DraftAttachmentProjection.AttachmentId))))
                throw new InvalidOperationException("Draft attachment projection scope is immutable.");
        }
    }
}
