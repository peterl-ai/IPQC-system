using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<WorkContext> WorkContexts => Set<WorkContext>();
    public DbSet<WorkContextAuditEvent> WorkContextAuditEvents => Set<WorkContextAuditEvent>();

    private void ConfigureWorkContexts(ModelBuilder b)
    {
        var c = b.Entity<WorkContext>();
        c.HasKey(x => x.Id);
        c.Property(x => x.ProofSecretHash).HasMaxLength(64).IsRequired();
        c.Property(x => x.EnvironmentId).HasMaxLength(200).IsRequired();
        c.Property(x => x.SecurityRevocationReason).HasMaxLength(2000);
        c.Property(x => x.Revision).IsConcurrencyToken();
        c.HasIndex(x => new { x.UserId, x.SecurityRevokedAtUtc });
        c.HasIndex(x => new { x.TaskId, x.UserId });
        c.HasIndex(x => new { x.EnvironmentId, x.TaskId });
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SecurityRevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<PatrolTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        c.ToTable(t => {
            t.HasCheckConstraint("CK_WorkContexts_Contract", "\"ContractVersion\" = 1 AND \"WorkflowVersion\" = 2 AND \"Revision\" >= 1 AND length(\"ProofSecretHash\") = 64 AND length(trim(\"EnvironmentId\")) BETWEEN 1 AND 200");
            t.HasCheckConstraint("CK_WorkContexts_Capture", "\"IssuedAtUtc\" < \"CaptureUntilUtc\" AND \"CaptureUntilUtc\" <= \"PersonalSessionExpiresAtUtc\"");
            t.HasCheckConstraint("CK_WorkContexts_Revocation", "(\"SecurityRevokedAtUtc\" IS NULL AND \"SecurityRevocationReason\" IS NULL AND \"SecurityRevokedByUserId\" IS NULL) OR (\"SecurityRevokedAtUtc\" IS NOT NULL AND \"SecurityRevocationReason\" IS NOT NULL AND length(trim(\"SecurityRevocationReason\")) > 0 AND \"SecurityRevokedByUserId\" IS NOT NULL)");
        });
        var a = b.Entity<WorkContextAuditEvent>();
        a.HasKey(x => x.Id);
        a.Property(x => x.Action).HasMaxLength(80).IsRequired();
        a.Property(x => x.ActorType).HasMaxLength(20).IsRequired();
        a.Property(x => x.ActorUsername).HasMaxLength(100).IsRequired();
        a.Property(x => x.Reason).HasMaxLength(2000);
        a.HasIndex(x => new { x.WorkContextId, x.OccurredAtUtc, x.Id });
        a.HasIndex(x => x.ChangeId);
        a.HasOne<WorkContext>().WithMany().HasForeignKey(x => x.WorkContextId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SourceUserId).OnDelete(DeleteBehavior.Restrict);
        a.ToTable(t => t.HasCheckConstraint("CK_WorkContextAuditEvents_Action", "\"Action\" IN ('ContextIssued','ContextSecurityRevoked') AND \"ActorType\" = 'User' AND \"AfterRevision\" >= 1"));
    }

    private void GuardWorkContexts()
    {
        if (ChangeTracker.Entries<WorkContextAuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("WorkContext audit is append-only.");
        foreach (var entry in ChangeTracker.Entries<WorkContext>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("WorkContext history cannot be deleted.");
            if (entry.State != EntityState.Modified) continue;
            string[] mutable = [nameof(WorkContext.Revision), nameof(WorkContext.SecurityRevokedAtUtc), nameof(WorkContext.SecurityRevocationReason), nameof(WorkContext.SecurityRevokedByUserId)];
            if (entry.Properties.Any(p => p.IsModified && !mutable.Contains(p.Metadata.Name)) ||
                entry.OriginalValues.GetValue<DateTime?>(nameof(WorkContext.SecurityRevokedAtUtc)) is not null)
                throw new InvalidOperationException("WorkContext identity and revocation are immutable.");
        }
    }
}
