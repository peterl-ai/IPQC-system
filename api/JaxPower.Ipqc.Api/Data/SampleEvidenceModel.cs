using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<SampleEvidenceOperation> SampleEvidenceOperations => Set<SampleEvidenceOperation>();
    public DbSet<SampleEvidenceProjection> SampleEvidenceProjections => Set<SampleEvidenceProjection>();
    private static void ConfigureSampleEvidence(ModelBuilder b)
    {
        var o = b.Entity<SampleEvidenceOperation>(); o.HasKey(x => new { x.TaskId, x.OperationId });
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.BindingId }).HasName("AK_SampleEvidence_Projection");
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.BindingId, x.SampleOperationId, x.ContentHash }).HasName("AK_SampleEvidence_Predecessor");
        o.HasIndex(x => new { x.TaskId, x.BatchId }).IsUnique().HasDatabaseName("IX_SampleEvidence_Batch");
        o.HasIndex(x => new { x.TaskId, x.DraftVersion, x.OperationId }).HasDatabaseName("IX_SampleEvidence_History");
        o.HasIndex(x => new { x.TaskId, x.TaskItemId, x.InspectionAttemptId }).HasDatabaseName("IX_SampleEvidence_Item");
        o.HasIndex(x => new { x.SampleId, x.TaskId, x.TaskItemId, x.InspectionAttemptId }).HasDatabaseName("IX_SampleEvidence_Sample");
        o.HasIndex(x => new { x.TaskId, x.PredecessorOperationId, x.BindingId, x.SampleOperationId, x.PredecessorHash }).HasDatabaseName("IX_SampleEvidence_Predecessor");
        o.HasIndex(x => new { x.TaskId, x.PhotoReferenceOperationId, x.AttachmentId, x.PhotoReferenceHash }).HasDatabaseName("IX_SampleEvidence_Photo");
        o.Property(x => x.EnvironmentId).HasMaxLength(200); o.Property(x => x.Kind).HasMaxLength(40); o.Property(x => x.TransitionKind).HasMaxLength(40);
        o.Property(x => x.SampleHash).HasMaxLength(64); o.Property(x => x.PhotoReferenceHash).HasMaxLength(64); o.Property(x => x.PredecessorHash).HasMaxLength(64); o.Property(x => x.ContentHash).HasMaxLength(64);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId }).HasConstraintName("FK_SampleEvidence_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<SharedSample>().WithMany().HasForeignKey(x => new { x.SampleId, x.TaskId, x.TaskItemId, x.InspectionAttemptId }).HasPrincipalKey(x => new { x.Id, x.TaskId, x.TaskItemId, x.InspectionAttemptId }).HasConstraintName("FK_SampleEvidence_Sample").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ContributionOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.SampleOperationId, x.SampleHash }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.ContentHash }).HasConstraintName("FK_SampleEvidence_Target").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<DraftAttachmentOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.PhotoReferenceOperationId, x.AttachmentId, x.PhotoReferenceHash }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.AttachmentId, x.ContentHash }).HasConstraintName("FK_SampleEvidence_Photo").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).HasConstraintName("FK_SampleEvidence_Source").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).HasConstraintName("FK_SampleEvidence_Contributor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_SampleEvidence_Actor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<SampleEvidenceOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.BindingId }).HasConstraintName("FK_SampleEvidence_Root").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<SampleEvidenceOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.PredecessorOperationId, x.BindingId, x.SampleOperationId, x.PredecessorHash }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.BindingId, x.SampleOperationId, x.ContentHash }).HasConstraintName("FK_SampleEvidence_Predecessor").OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t => t.HasCheckConstraint("CK_SampleEvidence_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1 AND \"DraftVersion\">0 AND length(\"ContentHash\")=64 AND length(\"SampleHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND \"TransitionKind\" IN ('InitialNGCapture','NGToOKCorrection','NGToNGCorrection','OKToNGCorrection') AND ((\"Kind\"='BindSampleEvidence' AND \"OperationId\"=\"BindingId\" AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL) OR (\"Kind\" IN ('CorrectSampleEvidence','WithdrawSampleEvidence') AND \"OperationId\"<>\"BindingId\" AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64)) AND ((\"Kind\"='WithdrawSampleEvidence' AND \"PhotoReferenceOperationId\" IS NULL AND \"AttachmentId\" IS NULL AND \"PhotoReferenceHash\" IS NULL) OR (\"Kind\"<>'WithdrawSampleEvidence' AND \"PhotoReferenceOperationId\" IS NOT NULL AND \"AttachmentId\" IS NOT NULL AND \"PhotoReferenceHash\" IS NOT NULL AND length(\"PhotoReferenceHash\")=64))"));
        var p = b.Entity<SampleEvidenceProjection>(); p.HasKey(x => new { x.TaskId, x.BindingId });
        p.HasIndex(x => new { x.TaskId, x.LastOperationId, x.BindingId }).HasDatabaseName("IX_SampleEvidenceProjection_Last");
        p.HasOne<SampleEvidenceOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.LastOperationId, x.BindingId }).HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.BindingId }).HasConstraintName("FK_SampleEvidenceProjection_Last").OnDelete(DeleteBehavior.Restrict);
    }
    private void GuardSampleEvidence()
    {
        foreach (var e in ChangeTracker.Entries()) {
            if (e.Entity is SampleEvidenceOperation && e.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Sample evidence binding history is immutable.");
            if (e.Entity is SampleEvidenceProjection && (e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name is nameof(SampleEvidenceProjection.TaskId) or nameof(SampleEvidenceProjection.BindingId)))) throw new InvalidOperationException("Binding projection identity is immutable.");
        }
    }
}
