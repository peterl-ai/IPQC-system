using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<PicRosterEntry> PicRosterEntries => Set<PicRosterEntry>();
    public DbSet<PicRosterRevision> PicRosterRevisions => Set<PicRosterRevision>();
    private static void ConfigurePicRoster(ModelBuilder b) {
        var p = b.Entity<PicRosterEntry>(); p.HasKey(x => x.PicId);
        p.ToTable(t => t.HasCheckConstraint("CK_Pic_Identity", "\"PicId\"<>'00000000-0000-0000-0000-000000000000'"));
        var r = b.Entity<PicRosterRevision>(); r.HasKey(x => new { x.PicId, x.Revision });
        r.HasIndex(x => x.RequestId).IsUnique(); r.HasIndex(x => x.ActorUserId);
        r.Property(x => x.Name).HasMaxLength(200); r.Property(x => x.Department).HasMaxLength(200);
        r.Property(x => x.Action).HasMaxLength(16); r.Property(x => x.RequestHash).HasMaxLength(64);
        r.Property(x => x.ActorUsername).HasMaxLength(100); r.Property(x => x.EnvironmentId).HasMaxLength(200);
        r.HasOne<PicRosterEntry>().WithMany().HasForeignKey(x => x.PicId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        r.ToTable(t => t.HasCheckConstraint("CK_Pic_Revision", "\"Revision\">0 AND \"ContractVersion\"=1 AND \"RequestId\"<>'00000000-0000-0000-0000-000000000000' AND length(\"RequestHash\")=64 AND length(\"Name\") BETWEEN 1 AND 200 AND length(trim(\"Name\"))>0 AND length(\"Department\") BETWEEN 1 AND 200 AND length(trim(\"Department\"))>0 AND length(\"ActorUsername\") BETWEEN 1 AND 100 AND length(\"EnvironmentId\") BETWEEN 1 AND 200 AND \"ActorPersonalSessionId\"<>'00000000-0000-0000-0000-000000000000' AND \"Action\" IN ('Created','Updated','Activated','Deactivated')"));
    }
    private void GuardPicRoster() {
        if (ChangeTracker.Entries<PicRosterEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PicRosterRevision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("PIC identity and revision history are immutable.");
    }
}
