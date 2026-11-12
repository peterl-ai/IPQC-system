using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<SharedDraft> SharedDrafts => Set<SharedDraft>();
    public DbSet<SharedSample> SharedSamples => Set<SharedSample>();
    public DbSet<SharedSampleProjection> SharedSampleProjections => Set<SharedSampleProjection>();
    public DbSet<ContributionOperation> ContributionOperations => Set<ContributionOperation>();
    public DbSet<ContributionDependency> ContributionDependencies => Set<ContributionDependency>();
    public DbSet<AcceptedBatchReceipt> AcceptedBatchReceipts => Set<AcceptedBatchReceipt>();
    public DbSet<SharedAcceptanceAudit> SharedAcceptanceAudits => Set<SharedAcceptanceAudit>();

    private static void ConfigureSharedDrafts(ModelBuilder b)
    {
        var d=b.Entity<SharedDraft>(); d.HasKey(x=>x.TaskId); d.Property(x=>x.DraftVersion).IsConcurrencyToken();
        d.HasOne<PatrolTask>().WithOne(x=>x.SharedDraft).HasForeignKey<SharedDraft>(x=>x.TaskId).OnDelete(DeleteBehavior.Restrict);
        d.ToTable(t=>t.HasCheckConstraint("CK_SharedDrafts_Version","\"DraftVersion\" >= 0"));
        var s=b.Entity<SharedSample>(); s.HasKey(x=>x.Id);
        s.HasAlternateKey(x=>new {x.Id,x.TaskId,x.TaskItemId,x.InspectionAttemptId}); s.HasAlternateKey(x=>new{x.Id,x.TaskId});
        s.HasIndex(x=>new{x.InspectionAttemptId,x.ClientSampleId}).IsUnique();
        s.HasIndex(x=>new{x.InspectionAttemptId,x.SequenceNo}).IsUnique();
        s.HasOne<SharedDraft>().WithMany().HasForeignKey(x=>x.TaskId).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x=>new{x.InspectionAttemptId,x.TaskItemId})
            .HasPrincipalKey(x=>new{x.Id,x.PatrolTaskItemId}).HasConstraintName("FK_SharedSamples_AttemptScope").OnDelete(DeleteBehavior.Restrict);
        s.ToTable(t=>t.HasCheckConstraint("CK_SharedSamples_Identity","\"Id\" <> '00000000-0000-0000-0000-000000000000' AND \"ClientSampleId\" <> '00000000-0000-0000-0000-000000000000' AND \"SequenceNo\" >= 1"));
        var batch=b.Entity<AcceptedBatchReceipt>(); batch.HasKey(x=>new{x.TaskId,x.BatchId});
        batch.Property(x=>x.RequestFingerprint).HasMaxLength(64);
        batch.HasOne<SharedDraft>().WithMany().HasForeignKey(x=>x.TaskId).OnDelete(DeleteBehavior.Restrict);
        batch.ToTable(t=>t.HasCheckConstraint("CK_AcceptedBatchReceipts_Version","\"DraftVersion\" > 0 AND length(\"RequestFingerprint\") = 64 AND length(\"ReceiptJson\") > 0"));
        var o=b.Entity<ContributionOperation>(); o.HasKey(x=>new{x.TaskId,x.OperationId});
        o.HasAlternateKey(x=>new{x.TaskId,x.OperationId,x.SampleId}); o.HasAlternateKey(x=>new{x.TaskId,x.OperationId,x.ContentHash});
        o.Property(x=>x.Kind).HasMaxLength(40); o.Property(x=>x.ContentHash).HasMaxLength(64); o.Property(x=>x.EnvironmentId).HasMaxLength(200);
        o.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x=>new{x.TaskId,x.BatchId}).HasConstraintName("FK_ContributionOperations_Batch").OnDelete(DeleteBehavior.Restrict);
        o.HasOne<SharedSample>().WithMany().HasForeignKey(x=>new{x.SampleId,x.TaskId,x.TaskItemId,x.InspectionAttemptId})
            .HasPrincipalKey(x=>new{x.Id,x.TaskId,x.TaskItemId,x.InspectionAttemptId}).HasConstraintName("FK_ContributionOperations_SampleScope").OnDelete(DeleteBehavior.Restrict);
        o.HasIndex(x=>new{x.SampleId,x.TaskId,x.TaskItemId,x.InspectionAttemptId}).HasDatabaseName("IX_ContributionOperations_SampleScope");
        o.HasOne<WorkContext>().WithMany().HasForeignKey(x=>x.SourceWorkContextId).OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ContributorUserId).OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        o.HasOne<ProductionLine>().WithMany().HasForeignKey(x=>x.LineId).OnDelete(DeleteBehavior.Restrict);
        o.ToTable(t=>t.HasCheckConstraint("CK_ContributionOperations_Contract","\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"Kind\" IN ('CaptureSample','CorrectSample','RemoveSample') AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0"));
        var dep=b.Entity<ContributionDependency>(); dep.HasKey(x=>new{x.TaskId,x.OperationId,x.DependencyId}); dep.Property(x=>x.ContentHash).HasMaxLength(64);
        dep.HasOne<ContributionOperation>().WithMany().HasForeignKey(x=>new{x.TaskId,x.OperationId}).HasConstraintName("FK_ContributionDependencies_Operation").OnDelete(DeleteBehavior.Restrict);
        dep.HasOne<ContributionOperation>().WithMany().HasForeignKey(x=>new{x.TaskId,x.DependencyId,x.ContentHash})
            .HasPrincipalKey(x=>new{x.TaskId,x.OperationId,x.ContentHash}).HasConstraintName("FK_ContributionDependencies_Reference").OnDelete(DeleteBehavior.Restrict);
        dep.ToTable(t=>t.HasCheckConstraint("CK_ContributionDependencies_NoSelf","\"OperationId\" <> \"DependencyId\""));
        var p=b.Entity<SharedSampleProjection>(); p.HasKey(x=>x.SampleId);
        p.Property(x=>x.InspectionType).HasMaxLength(20); p.Property(x=>x.Judgment).HasMaxLength(2); p.Property(x=>x.Barcode).HasMaxLength(200);
        p.HasOne<SharedSample>().WithMany().HasForeignKey(x=>new{x.SampleId,x.TaskId}).HasPrincipalKey(x=>new{x.Id,x.TaskId})
            .HasConstraintName("FK_SharedSampleProjections_Sample").OnDelete(DeleteBehavior.Restrict);
        p.HasOne<ContributionOperation>().WithMany().HasForeignKey(x=>new{x.TaskId,x.LastOperationId,x.SampleId})
            .HasPrincipalKey(x=>new{x.TaskId,x.OperationId,x.SampleId}).HasConstraintName("FK_SharedSampleProjections_LastOperation").OnDelete(DeleteBehavior.Restrict);
        p.HasOne<ContributionOperation>().WithMany().HasForeignKey(x=>new{x.TaskId,x.ValueOperationId,x.SampleId})
            .HasPrincipalKey(x=>new{x.TaskId,x.OperationId,x.SampleId}).HasConstraintName("FK_SharedSampleProjections_ValueOperation").OnDelete(DeleteBehavior.Restrict);
        p.ToTable(t=>t.HasCheckConstraint("CK_SharedSampleProjections_Value","\"Judgment\" IN ('OK','NG') AND ((\"InspectionType\" = 'Qualitative' AND \"InspectionValue\" IS NULL AND \"Barcode\" IS NULL) OR (\"InspectionType\" = 'Quantitative' AND \"InspectionValue\" IS NOT NULL))"));
        var a=b.Entity<SharedAcceptanceAudit>(); a.HasKey(x=>x.Id);
        a.HasOne<AcceptedBatchReceipt>().WithMany().HasForeignKey(x=>new{x.TaskId,x.BatchId}).HasConstraintName("FK_SharedAcceptanceAudits_Batch").OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        a.ToTable(t=>t.HasCheckConstraint("CK_SharedAcceptanceAudits_Version","\"BeforeVersion\" >= 0 AND \"AfterVersion\" = \"BeforeVersion\" + 1 AND \"NewOperationCount\" BETWEEN 1 AND 50"));
    }

    private void GuardSharedDrafts()
    {
        Type[] immutable=[typeof(SharedSample),typeof(ContributionOperation),typeof(ContributionDependency),typeof(AcceptedBatchReceipt),typeof(SharedAcceptanceAudit)];
        if(ChangeTracker.Entries().Any(e=>immutable.Contains(e.Entity.GetType()) && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Shared acceptance identity, ledger, receipts and audit are immutable.");
        foreach(var e in ChangeTracker.Entries<SharedDraft>()) {
            if(e.State==EntityState.Deleted || e.State==EntityState.Modified &&
                (e.OriginalValues.GetValue<long>(nameof(SharedDraft.DraftVersion))==long.MaxValue ||
                 e.Entity.DraftVersion!=e.OriginalValues.GetValue<long>(nameof(SharedDraft.DraftVersion))+1))
                throw new InvalidOperationException("Shared draft version must advance once and retain history.");
        }
        foreach(var e in ChangeTracker.Entries<SharedSampleProjection>())
            if(e.State==EntityState.Deleted || e.State==EntityState.Modified && e.Properties.Any(p=>p.IsModified && p.Metadata.Name is nameof(SharedSampleProjection.TaskId) or nameof(SharedSampleProjection.SampleId)))
                throw new InvalidOperationException("Shared sample scope cannot change or disappear.");
    }
}
