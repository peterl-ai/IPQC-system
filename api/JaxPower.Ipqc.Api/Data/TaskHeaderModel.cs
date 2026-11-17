using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<TaskHeaderOperation> TaskHeaderOperations => Set<TaskHeaderOperation>();
    public DbSet<TaskHeaderProjection> TaskHeaderProjections => Set<TaskHeaderProjection>();
    private static void ConfigureTaskHeaders(ModelBuilder b) {
        var o = b.Entity<TaskHeaderOperation>(); o.HasKey(x => new { x.TaskId, x.OperationId });
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.Field }).HasName("AK_Header_ProjectionScope");
        o.HasAlternateKey(x => new { x.TaskId, x.OperationId, x.Field, x.ContentHash }).HasName("AK_Header_PredecessorScope");
        o.HasIndex(x => new { x.TaskId, x.BatchId }).IsUnique().HasDatabaseName("IX_Header_Batch");
        o.HasIndex(x => new { x.TaskId, x.DraftVersion }).IsUnique().HasDatabaseName("IX_Header_Version");
        o.HasIndex(x => new { x.TaskId, x.PredecessorOperationId, x.Field, x.PredecessorHash }).HasDatabaseName("IX_Header_Predecessor");
        o.Property(x => x.Field).HasMaxLength(24); o.Property(x => x.Kind).HasMaxLength(40);
        o.Property(x => x.Value).HasMaxLength(200); o.Property(x => x.EnvironmentId).HasMaxLength(200);
        o.Property(x => x.ContentHash).HasMaxLength(64); o.Property(x => x.PredecessorHash).HasMaxLength(64);
        o.HasOne<SharedDraft>().WithMany().HasForeignKey(x => x.TaskId).HasConstraintName("FK_Header_Draft").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x => new { x.TaskId, x.BatchId }).HasConstraintName("FK_Header_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.SourceWorkContextId).HasConstraintName("FK_Header_Source").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ContributorUserId).HasConstraintName("FK_Header_Contributor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_Header_Actor").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<TaskHeaderOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.PredecessorOperationId, x.Field, x.PredecessorHash })
            .HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.Field, x.ContentHash }).HasConstraintName("FK_Header_Predecessor").OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t => t.HasCheckConstraint("CK_Header_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1 AND \"BaseDraftVersion\">=0 AND \"OriginLocalEditVersion\">=0 AND \"DraftVersion\">0 AND \"DraftVersion\"=\"BaseDraftVersion\"+1 AND \"Field\" IN ('Customer','Production','ProductMo') AND (\"Value\" IS NULL OR length(\"Value\")<=200) AND length(\"ContentHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND ((\"Kind\"='CaptureBasicInformationField' AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL) OR (\"Kind\"='CorrectBasicInformationField' AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64))"));
        var p = b.Entity<TaskHeaderProjection>(); p.HasKey(x => new { x.TaskId, x.Field }); p.Property(x => x.Field).HasMaxLength(24);
        p.HasOne<TaskHeaderOperation>().WithMany().HasForeignKey(x => new { x.TaskId, x.LastOperationId, x.Field })
            .HasPrincipalKey(x => new { x.TaskId, x.OperationId, x.Field }).HasConstraintName("FK_HeaderProjection_Operation").OnDelete(DeleteBehavior.Restrict);
    }
    private void GuardTaskHeaders() {
        if (ChangeTracker.Entries<TaskHeaderOperation>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Task Basic Information history is immutable.");
        if (ChangeTracker.Entries<TaskHeaderProjection>().Any(e => e.State == EntityState.Deleted || e.State == EntityState.Modified &&
            e.Properties.Any(p => p.IsModified && p.Metadata.Name is nameof(TaskHeaderProjection.TaskId) or nameof(TaskHeaderProjection.Field))))
            throw new InvalidOperationException("Task Basic Information projection scope is immutable.");
    }
}
