using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<NgPicSelectionOperation> NgPicSelectionOperations => Set<NgPicSelectionOperation>();
    public DbSet<NgPicSelectionMember> NgPicSelectionMembers => Set<NgPicSelectionMember>();
    public DbSet<NgPicSelectionProjection> NgPicSelectionProjections => Set<NgPicSelectionProjection>();
    private static void ConfigureNgPicSelections(ModelBuilder b) {
        var o = b.Entity<NgPicSelectionOperation>(); o.HasKey(x => new { x.TaskId, x.OperationId });
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId }).HasName("AK_NgPic_Projection");
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId, x.ContentHash }).HasName("AK_NgPic_Predecessor");
        o.HasIndex(x => new { x.TaskId, x.BatchId }).IsUnique().HasDatabaseName("IX_NgPic_Batch");
        o.HasIndex(x => new { x.TaskId, x.DraftVersion }).IsUnique().HasDatabaseName("IX_NgPic_Version");
        o.HasIndex(x => new { x.TaskId, x.PredecessorOperationId, x.TaskItemId, x.InspectionAttemptId, x.PredecessorHash }).HasDatabaseName("IX_NgPic_Predecessor");
        o.Property(x => x.EnvironmentId).HasMaxLength(200); o.Property(x => x.Kind).HasMaxLength(32); o.Property(x => x.NgKind).HasMaxLength(32);
        o.Property(x => x.ContentHash).HasMaxLength(64); o.Property(x => x.PredecessorHash).HasMaxLength(64);
        o.HasOne<SharedDraft>().WithMany().HasForeignKey(x => x.TaskId).HasConstraintName("FK_NgPic_Draft").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId }).HasConstraintName("FK_NgPic_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x => new { x.InspectionAttemptId, x.TaskItemId }).HasPrincipalKey(x => new { x.Id, x.PatrolTaskItemId }).HasConstraintName("FK_NgPic_Attempt").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).HasConstraintName("FK_NgPic_Source").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).HasConstraintName("FK_NgPic_Contributor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_NgPic_Actor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<NgPicSelectionOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.PredecessorOperationId, x.TaskItemId, x.InspectionAttemptId, x.PredecessorHash })
            .HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId, x.ContentHash }).HasConstraintName("FK_NgPic_Predecessor").OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t => t.HasCheckConstraint("CK_NgPic_Operation", "\"OperationId\"<>'00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1 AND \"BaseDraftVersion\">=0 AND \"OriginLocalEditVersion\">=0 AND \"DraftVersion\">0 AND \"DraftVersion\"=\"BaseDraftVersion\"+1 AND \"PicCount\" BETWEEN 0 AND 100 AND \"NgKind\" IN ('OrdinaryNG','UnableToMeasureNG') AND length(\"NgBasisJson\")>0 AND length(\"ContentHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND ((\"Kind\"='SelectNgPics' AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL) OR (\"Kind\"='CorrectNgPics' AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64))"));
        var m = b.Entity<NgPicSelectionMember>(); m.HasKey(x => new { x.TaskId, x.OperationId, x.PicId });
        m.Property(x => x.Name).HasMaxLength(200); m.Property(x => x.Department).HasMaxLength(200);
        m.HasOne<NgPicSelectionOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.OperationId }).HasConstraintName("FK_NgPicMember_Operation").OnDelete(DeleteBehavior.Restrict);
        m.HasOne<PicRosterRevision>().WithMany().HasForeignKey(x => new { x.PicId, Revision = x.RosterRevision }).HasConstraintName("FK_NgPicMember_Roster").OnDelete(DeleteBehavior.Restrict);
        m.ToTable(t => t.HasCheckConstraint("CK_NgPicMember_Snapshot", "\"RosterRevision\">0 AND length(\"Name\") BETWEEN 1 AND 200 AND length(\"Department\") BETWEEN 1 AND 200"));
        var p = b.Entity<NgPicSelectionProjection>(); p.HasKey(x => new { x.TaskId, x.TaskItemId, x.InspectionAttemptId });
        p.HasIndex(x => new { x.TaskId, x.LastOperationId, x.TaskItemId, x.InspectionAttemptId }).HasDatabaseName("IX_NgPicProjection_Last");
        p.HasOne<NgPicSelectionOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.LastOperationId, x.TaskItemId, x.InspectionAttemptId })
            .HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId }).HasConstraintName("FK_NgPicProjection_Operation").OnDelete(DeleteBehavior.Restrict);
    }
    private void GuardNgPicSelections() {
        if (ChangeTracker.Entries<NgPicSelectionOperation>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<NgPicSelectionMember>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("NG PIC selection history is immutable.");
        if (ChangeTracker.Entries<NgPicSelectionProjection>().Any(e => e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(NgPicSelectionProjection.LastOperationId))))
            throw new InvalidOperationException("NG PIC selection scope is immutable.");
    }
}
