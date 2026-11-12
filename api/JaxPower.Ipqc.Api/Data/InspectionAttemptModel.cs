using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<InspectionAttempt> InspectionAttempts => Set<InspectionAttempt>();
    public DbSet<InspectionAttemptApplicability> InspectionAttemptApplicabilities => Set<InspectionAttemptApplicability>();

    private static void ConfigureInspectionAttempts(ModelBuilder b)
    {
        var a = b.Entity<InspectionAttempt>();
        a.HasKey(x => x.Id);
        a.HasAlternateKey(x => new { x.Id, x.PatrolTaskItemId });
        a.Property(x => x.Kind).HasMaxLength(40);
        a.Property(x => x.CreationSource).HasMaxLength(40);
        a.Property(x => x.CreatedByUsername).HasMaxLength(100);
        a.Property(x => x.InitializationReason).HasMaxLength(2000);
        a.Property(x => x.FrozenSampleCount).HasMaxLength(4000);
        a.HasIndex(x => x.PatrolTaskItemId).IsUnique().HasFilter("\"Kind\" = 'Initial'");
        a.HasIndex(x => x.CreationEventId);
        a.HasOne(x => x.PatrolTaskItem).WithMany(x => x.InspectionAttempts)
            .HasForeignKey(x => x.PatrolTaskItemId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        a.ToTable(t => {
            t.HasCheckConstraint("CK_InspectionAttempts_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000' AND \"CreationEventId\" <> '00000000-0000-0000-0000-000000000000' AND \"Kind\" = 'Initial' AND \"PlanningSnapshotVersion\" = 1 AND \"MinimumSampleCount\" BETWEEN 1 AND 50");
            t.HasCheckConstraint("CK_InspectionAttempts_Provenance", "(\"CreationSource\" = 'TaskGeneration' AND \"CreatedByUserId\" IS NULL AND \"CreatedByUsername\" IS NULL AND \"CreatedByPersonalSessionId\" IS NULL AND \"InitializationReason\" IS NULL) OR (\"CreationSource\" = 'ExplicitInitialization' AND \"CreatedByUserId\" IS NOT NULL AND \"CreatedByUsername\" IS NOT NULL AND length(trim(\"CreatedByUsername\")) > 0 AND \"CreatedByPersonalSessionId\" IS NOT NULL AND \"CreatedByPersonalSessionId\" <> '00000000-0000-0000-0000-000000000000' AND \"InitializationReason\" IS NOT NULL AND length(trim(\"InitializationReason\")) BETWEEN 1 AND 2000)");
        });
        var c = b.Entity<InspectionAttemptApplicability>();
        c.HasKey(x => x.PatrolTaskItemId);
        c.HasIndex(x => x.InspectionAttemptId).IsUnique();
        c.HasOne(x => x.PatrolTaskItem).WithOne(x => x.CurrentInspectionAttempt)
            .HasForeignKey<InspectionAttemptApplicability>(x => x.PatrolTaskItemId)
            .HasConstraintName("FK_AttemptApplicabilities_TaskItem").OnDelete(DeleteBehavior.Restrict);
        c.HasOne(x => x.InspectionAttempt).WithMany()
            .HasForeignKey(x => new { x.InspectionAttemptId, x.PatrolTaskItemId })
            .HasPrincipalKey(x => new { x.Id, x.PatrolTaskItemId })
            .HasConstraintName("FK_AttemptApplicabilities_AttemptScope").OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.InspectionAttemptId, x.PatrolTaskItemId })
            .HasDatabaseName("IX_AttemptApplicabilities_AttemptScope");
    }

    private void GuardInspectionAttempts()
    {
        if (ChangeTracker.Entries<InspectionAttempt>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Inspection attempt creation history is immutable.");
    }
}
