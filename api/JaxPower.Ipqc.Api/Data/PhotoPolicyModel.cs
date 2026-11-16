using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<PhotoPolicyDefinition> PhotoPolicyDefinitions => Set<PhotoPolicyDefinition>();
    public DbSet<PhotoPolicyConfirmation> PhotoPolicyConfirmations => Set<PhotoPolicyConfirmation>();
    public DbSet<PhotoPolicyState> PhotoPolicyStates => Set<PhotoPolicyState>();
    public DbSet<FrozenTaskPhotoPolicy> FrozenTaskPhotoPolicies => Set<FrozenTaskPhotoPolicy>();
    private static void ConfigurePhotoPolicies(ModelBuilder b)
    {
        var d = b.Entity<PhotoPolicyDefinition>(); d.HasKey(x => x.Id);
        d.HasAlternateKey(x => new { x.Id, x.StandardItemId }).HasName("AK_PhotoPolicyDefinition_Item");
        d.HasIndex(x => new { x.StandardItemId, x.Version }).IsUnique().HasDatabaseName("IX_PhotoPolicyDefinition_Version");
        d.Property(x => x.DefinitionHash).HasMaxLength(64);
        d.ToTable(t => t.HasCheckConstraint("CK_PhotoPolicyDefinition", "\"Version\">0 AND length(\"DefinitionHash\")=64 AND length(\"DefinitionJson\")>0"));
        // Source IDs intentionally have no FK to mutable/deletable Standard children: deletion must retain confirmation history.
        var c = b.Entity<PhotoPolicyConfirmation>(); c.HasKey(x => x.Id);
        c.HasAlternateKey(x => new { x.Id, x.DefinitionId, x.StandardItemId, x.PolicyVersion }).HasName("AK_PhotoPolicyConfirmation_State");
        c.HasIndex(x => x.RequestId).IsUnique(); c.HasIndex(x => new { x.StandardItemId, x.PolicyVersion }).IsUnique().HasDatabaseName("IX_PhotoPolicyConfirmation_Version");
        c.Property(x => x.Value).HasMaxLength(16); c.Property(x => x.EnvironmentId).HasMaxLength(200); c.Property(x => x.RequestHash).HasMaxLength(64);
        c.HasOne<PhotoPolicyDefinition>().WithMany().HasForeignKey(x => new { x.DefinitionId, x.StandardItemId }).HasPrincipalKey(x => new { x.Id, x.StandardItemId }).HasConstraintName("FK_PhotoPolicyConfirmation_Definition").OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        c.ToTable(t => t.HasCheckConstraint("CK_PhotoPolicyConfirmation", "\"Value\" IN ('Required','Optional') AND \"PolicyVersion\">0 AND length(\"RequestHash\")=64 AND length(\"ReceiptJson\")>0 AND length(\"EnvironmentId\") BETWEEN 1 AND 200"));
        var s = b.Entity<PhotoPolicyState>(); s.HasKey(x => x.StandardItemId);
        s.HasOne<PhotoPolicyDefinition>().WithMany().HasForeignKey(x => new { x.DefinitionId, x.StandardItemId }).HasPrincipalKey(x => new { x.Id, x.StandardItemId }).HasConstraintName("FK_PhotoPolicyState_Definition").OnDelete(DeleteBehavior.Restrict);
        s.HasOne<PhotoPolicyConfirmation>().WithMany().HasForeignKey(x => new { x.ConfirmationId, x.DefinitionId, x.StandardItemId, x.PolicyVersion }).HasPrincipalKey(x => new { x.Id, x.DefinitionId, x.StandardItemId, x.PolicyVersion }).HasConstraintName("FK_PhotoPolicyState_Confirmation").OnDelete(DeleteBehavior.Restrict);
        s.HasIndex(x => new { x.ConfirmationId, x.DefinitionId, x.StandardItemId, x.PolicyVersion }).HasDatabaseName("IX_PhotoPolicyState_Confirmation");
        s.ToTable(t => t.HasCheckConstraint("CK_PhotoPolicyState", "\"PolicyVersion\">=0 AND (\"IsPresent\" OR \"ConfirmationId\" IS NULL)"));
        var f = b.Entity<FrozenTaskPhotoPolicy>(); f.HasKey(x => x.TaskItemId); f.HasIndex(x => x.TaskId);
        f.HasOne<PatrolTaskItem>().WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        f.HasOne<PhotoPolicyConfirmation>().WithMany().HasForeignKey(x => x.ConfirmationId).HasConstraintName("FK_FrozenPhotoPolicy_Confirmation").OnDelete(DeleteBehavior.Restrict);
        f.Property(x => x.SnapshotHash).HasMaxLength(64);
        f.ToTable(t => t.HasCheckConstraint("CK_FrozenTaskPhotoPolicy", "length(\"SnapshotHash\")=64 AND length(\"SnapshotJson\")>0"));
    }
    private void GuardPhotoPolicies()
    {
        foreach (var e in ChangeTracker.Entries()) {
            if (e.Entity is PhotoPolicyDefinition or PhotoPolicyConfirmation or FrozenTaskPhotoPolicy && e.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Photo policy history is immutable.");
            if (e.Entity is PhotoPolicyState && (e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name is nameof(PhotoPolicyState.StandardId) or nameof(PhotoPolicyState.StandardItemId))))
                throw new InvalidOperationException("Photo policy scope is immutable.");
        }
    }
}
