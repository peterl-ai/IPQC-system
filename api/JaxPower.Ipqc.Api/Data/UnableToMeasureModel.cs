using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<AcceptedOperationIdentity> AcceptedOperationIdentities => Set<AcceptedOperationIdentity>();
    public DbSet<UnableToMeasureOperation> UnableToMeasureOperations => Set<UnableToMeasureOperation>();
    public DbSet<UnableToMeasureProjection> UnableToMeasureProjections => Set<UnableToMeasureProjection>();
    private static void ConfigureUnableToMeasure(ModelBuilder b)
    {
        var k = b.Entity<AcceptedOperationIdentity>(); k.HasKey(x => new { x.TaskId, x.OperationId });
        k.Property(x => x.Domain).HasMaxLength(64);
        k.HasOne<SharedDraft>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        k.ToTable(t => t.HasCheckConstraint("CK_AcceptedOperationIdentity_Domain", "length(\"Domain\") BETWEEN 1 AND 64"));
        var o = b.Entity<UnableToMeasureOperation>(); o.HasKey(x => new { x.TaskId, x.OperationId });
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.InspectionAttemptId, x.TaskItemId }).HasName("AK_Unable_ProjectionScope");
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.InspectionAttemptId, x.ContentHash }).HasName("AK_Unable_PredecessorScope");
        o.HasIndex(x => new { x.TaskId, x.BatchId }).IsUnique().HasDatabaseName("IX_Unable_Batch");
        o.HasIndex(x => new { x.TaskId, x.DraftVersion, x.OperationId }).HasDatabaseName("IX_Unable_History");
        o.HasIndex(x => new { x.TaskId, x.PredecessorOperationId, x.InspectionAttemptId, x.PredecessorHash }).HasDatabaseName("IX_Unable_Predecessor");
        o.HasIndex(x => new { x.TaskId, x.EvidenceOperationId, x.AttachmentId, x.EvidenceHash }).HasDatabaseName("IX_Unable_Evidence");
        o.Property(x => x.EnvironmentId).HasMaxLength(200); o.Property(x => x.Kind).HasMaxLength(40); o.Property(x => x.NgReason).HasMaxLength(2000);
        o.Property(x => x.ContentHash).HasMaxLength(64); o.Property(x => x.PredecessorHash).HasMaxLength(64); o.Property(x => x.EvidenceHash).HasMaxLength(64);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId }).HasConstraintName("FK_Unable_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x => new { x.InspectionAttemptId, x.TaskItemId }).HasPrincipalKey(x => new { x.Id, x.PatrolTaskItemId }).HasConstraintName("FK_Unable_Attempt").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).HasConstraintName("FK_Unable_Source").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).HasConstraintName("FK_Unable_Contributor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_Unable_Actor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<DraftAttachmentOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.EvidenceOperationId, x.AttachmentId, x.EvidenceHash }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.AttachmentId, x.ContentHash }).HasConstraintName("FK_Unable_Evidence").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<UnableToMeasureOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.PredecessorOperationId, x.InspectionAttemptId, x.PredecessorHash }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.InspectionAttemptId, x.ContentHash }).HasConstraintName("FK_Unable_Predecessor").OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t => t.HasCheckConstraint("CK_Unable_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0 AND \"Kind\" IN ('SetUnableToMeasureNG','CorrectUnableToMeasureNG','ClearUnableToMeasureNG') AND ((\"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL AND \"Kind\"='SetUnableToMeasureNG') OR (\"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64)) AND ((\"Kind\"='ClearUnableToMeasureNG' AND \"NgReason\" IS NULL AND \"EvidenceOperationId\" IS NULL AND \"AttachmentId\" IS NULL AND \"EvidenceHash\" IS NULL) OR (\"Kind\"<>'ClearUnableToMeasureNG' AND \"NgReason\" IS NOT NULL AND length(trim(\"NgReason\")) BETWEEN 1 AND 2000 AND \"EvidenceOperationId\" IS NOT NULL AND \"AttachmentId\" IS NOT NULL AND \"EvidenceHash\" IS NOT NULL AND length(\"EvidenceHash\")=64))"));
        var p = b.Entity<UnableToMeasureProjection>(); p.HasKey(x => new { x.TaskId, x.InspectionAttemptId });
        p.HasIndex(x => new { x.TaskId, x.LastOperationId, x.InspectionAttemptId, x.TaskItemId }).HasDatabaseName("IX_UnableProjection_Operation");
        p.HasOne<UnableToMeasureOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.LastOperationId, x.InspectionAttemptId, x.TaskItemId }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.InspectionAttemptId, x.TaskItemId }).HasConstraintName("FK_UnableProjection_Operation").OnDelete(DeleteBehavior.Restrict);
    }
    private void GuardUnableToMeasure()
    {
        foreach (var e in ChangeTracker.Entries()) {
            if (e.Entity is AcceptedOperationIdentity or UnableToMeasureOperation && e.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("UnableToMeasureNG acceptance history is immutable.");
            if (e.Entity is UnableToMeasureProjection && (e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name is nameof(UnableToMeasureProjection.TaskId) or nameof(UnableToMeasureProjection.TaskItemId) or nameof(UnableToMeasureProjection.InspectionAttemptId))))
                throw new InvalidOperationException("Item disposition projection scope is immutable.");
        }
    }
}
