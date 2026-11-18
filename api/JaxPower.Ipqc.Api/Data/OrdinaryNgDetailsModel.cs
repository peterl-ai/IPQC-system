using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<OrdinaryNgDetailsOperation> OrdinaryNgDetailsOperations => Set<OrdinaryNgDetailsOperation>();

    private static void ConfigureOrdinaryNgDetails(ModelBuilder builder)
    {
        var operation = builder.Entity<OrdinaryNgDetailsOperation>();
        operation.HasKey(x => new { x.TaskId, x.OperationId });
        operation.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId, x.ContentHash })
            .HasName("AK_OrdNg_Predecessor");
        operation.HasIndex(x => new { x.TaskId, x.BatchId }).IsUnique().HasDatabaseName("IX_OrdNg_Batch");
        operation.HasIndex(x => new { x.TaskId, x.DraftVersion }).IsUnique().HasDatabaseName("IX_OrdNg_Version");
        operation.HasIndex(x => new { x.TaskId, x.TaskItemId, x.InspectionAttemptId, x.DraftVersion }).HasDatabaseName("IX_OrdNg_Scope");
        operation.HasIndex(x => new { x.TaskId, x.PredecessorOperationId, x.TaskItemId, x.InspectionAttemptId, x.PredecessorHash })
            .HasDatabaseName("IX_OrdNg_Predecessor");
        operation.Property(x => x.EnvironmentId).HasMaxLength(200);
        operation.Property(x => x.Kind).HasMaxLength(32);
        operation.Property(x => x.ContentHash).HasMaxLength(64);
        operation.Property(x => x.PredecessorHash).HasMaxLength(64);
        operation.Property(x => x.NgType).HasMaxLength(2000);
        operation.Property(x => x.NgReason).HasMaxLength(2000);
        operation.Property(x => x.Remarks).HasMaxLength(2000);

        operation.HasOne<SharedDraft>().WithMany().HasForeignKey(x => x.TaskId).HasConstraintName("FK_OrdNg_Draft").OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId }).HasConstraintName("FK_OrdNg_Batch").OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x => new { x.InspectionAttemptId, x.TaskItemId })
            .HasPrincipalKey(x => new { x.Id, x.PatrolTaskItemId }).HasConstraintName("FK_OrdNg_Attempt").OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).HasConstraintName("FK_OrdNg_Source").OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).HasConstraintName("FK_OrdNg_Contributor").OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_OrdNg_Actor").OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<OrdinaryNgDetailsOperation>().WithMany()
            .HasForeignKey(x => new { x.TaskId, x.PredecessorOperationId, x.TaskItemId, x.InspectionAttemptId, x.PredecessorHash })
            .HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId, x.ContentHash })
            .HasConstraintName("FK_OrdNg_Predecessor").OnDelete(DeleteBehavior.Restrict);

        operation.ToTable(t => t.HasCheckConstraint("CK_OrdNg_Operation", """
            "OperationId"<>'00000000-0000-0000-0000-000000000000' AND "WorkflowVersion"=2 AND "CanonicalizationVersion"=1
            AND "BaseDraftVersion">=0 AND "OriginLocalEditVersion">=0 AND "DraftVersion">0 AND "DraftVersion"="BaseDraftVersion"+1
            AND length("ContentHash")=64 AND length("CanonicalPayload")>0 AND length("ReceiptJson")>0 AND length("NgBasisJson")>0
            AND (("Kind"='SetOrdinaryNgDetails' AND "PredecessorOperationId" IS NULL AND "PredecessorHash" IS NULL)
                 OR ("Kind"='CorrectOrdinaryNgDetails' AND "PredecessorOperationId" IS NOT NULL AND "PredecessorHash" IS NOT NULL AND length("PredecessorHash")=64))
            AND ("NgType" IS NULL OR length("NgType")<=2000) AND ("NgReason" IS NULL OR length("NgReason")<=2000)
            AND ("Remarks" IS NULL OR length("Remarks")<=2000)
            """));
    }

    private void GuardOrdinaryNgDetails()
    {
        if (ChangeTracker.Entries<OrdinaryNgDetailsOperation>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Accepted ordinary NG details are immutable.");
    }
}
