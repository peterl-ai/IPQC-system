using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<ItemRemovalOperation> ItemRemovalOperations => Set<ItemRemovalOperation>();
    public DbSet<SharedOperationIdentity> SharedOperationIdentities => Set<SharedOperationIdentity>();

    private static void ConfigureItemRemovals(ModelBuilder b)
    {
        var identity = b.Entity<SharedOperationIdentity>();
        identity.HasKey(x => new { x.TaskId, x.OperationId });
        identity.Property(x => x.Domain).HasMaxLength(20);
        identity.HasOne<SharedDraft>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        identity.ToTable(t => t.HasCheckConstraint("CK_SharedOperationIdentities_Domain", "\"Domain\" IN ('Sample','ItemRemoval')"));
        var o = b.Entity<ItemRemovalOperation>();
        o.HasKey(x => new { x.TaskId, x.OperationId });
        o.HasIndex(x => new { x.TaskId, x.BatchId }).IsUnique();
        o.HasIndex(x => x.InspectionAttemptId).IsUnique();
        o.Property(x => x.Kind).HasMaxLength(40);
        o.Property(x => x.ContentHash).HasMaxLength(64);
        o.Property(x => x.EnvironmentId).HasMaxLength(200);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId })
            .HasConstraintName("FK_ItemRemovals_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x => new { x.InspectionAttemptId, x.TaskItemId })
            .HasPrincipalKey(x => new { x.Id, x.PatrolTaskItemId }).HasConstraintName("FK_ItemRemovals_Attempt").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t => t.HasCheckConstraint("CK_ItemRemovals_Contract",
            "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"Kind\" = 'RemoveItem' AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0"));
    }

    private void GuardItemRemovals()
    {
        if (ChangeTracker.Entries().Any(e => (e.Entity is ItemRemovalOperation or SharedOperationIdentity) &&
            e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Item removal history and operation identities are immutable.");
    }
}
