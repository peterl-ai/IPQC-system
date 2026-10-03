using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed class IpqcDbContext(DbContextOptions<IpqcDbContext> options) : DbContext(options)
{
    public DbSet<PatrolStandard> PatrolStandards => Set<PatrolStandard>();
    public DbSet<PatrolStandardItem> PatrolStandardItems => Set<PatrolStandardItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var standard = modelBuilder.Entity<PatrolStandard>();
        standard.HasKey(x => x.Id);
        standard.Property(x => x.PatrolStandardName).HasMaxLength(200).IsRequired();
        standard.Property(x => x.LineName).HasMaxLength(200).IsRequired();
        standard.Property(x => x.StandardNo).HasMaxLength(50);
        standard.Property(x => x.FactoryCode).HasMaxLength(100);
        standard.Property(x => x.FactoryName).HasMaxLength(200);
        standard.Property(x => x.WorkshopCode).HasMaxLength(100);
        standard.Property(x => x.LineCode).HasMaxLength(100);
        standard.Property(x => x.MaterialCode).HasMaxLength(100);
        standard.Property(x => x.CreatedBy).HasMaxLength(100);
        standard.Property(x => x.UpdatedBy).HasMaxLength(100);
        standard.HasIndex(x => x.PatrolStandardName);
        standard.HasIndex(x => x.FactoryCode);
        standard.HasIndex(x => x.LineCode);
        standard.HasMany(x => x.InspectionItems).WithOne(x => x.PatrolStandard).HasForeignKey(x => x.PatrolStandardId).OnDelete(DeleteBehavior.Cascade);

        var item = modelBuilder.Entity<PatrolStandardItem>();
        item.HasKey(x => x.Id);
        item.HasIndex(x => new { x.PatrolStandardId, x.SequenceNo }).IsUnique();
        foreach (var property in typeof(PatrolStandardItem).GetProperties().Where(p => p.PropertyType == typeof(string)))
            item.Property(property.Name).HasMaxLength(2000);
    }
}
