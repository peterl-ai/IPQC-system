using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed class IpqcDbContext(DbContextOptions<IpqcDbContext> options) : DbContext(options)
{
    public DbSet<PatrolStandard> PatrolStandards => Set<PatrolStandard>();
    public DbSet<PatrolStandardItem> PatrolStandardItems => Set<PatrolStandardItem>();
    public DbSet<PatrolPlan> PatrolPlans => Set<PatrolPlan>();
    public DbSet<PatrolPlanAssignee> PatrolPlanAssignees => Set<PatrolPlanAssignee>();
    public DbSet<PatrolTask> PatrolTasks => Set<PatrolTask>();

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

        var plan = modelBuilder.Entity<PatrolPlan>();
        plan.HasKey(x => x.Id);
        plan.Property(x => x.PlanNo).HasMaxLength(50).IsRequired();
        plan.HasIndex(x => x.PlanNo).IsUnique();
        plan.Property(x => x.PlanName).HasMaxLength(200).IsRequired();
        plan.Property(x => x.FactoryCode).HasMaxLength(100);
        plan.Property(x => x.FactoryName).HasMaxLength(200);
        plan.Property(x => x.LineCode).HasMaxLength(100);
        plan.Property(x => x.LineName).HasMaxLength(200);
        plan.Property(x => x.ScheduleDefinition).IsRequired();
        plan.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        plan.Property(x => x.CreatedBy).HasMaxLength(100);
        plan.Property(x => x.UpdatedBy).HasMaxLength(100);
        plan.HasIndex(x => new { x.IsEnabled, x.EffectiveStartUtc, x.EffectiveEndUtc });
        plan.HasOne(x => x.PatrolStandard).WithMany().HasForeignKey(x => x.PatrolStandardId).OnDelete(DeleteBehavior.Restrict);
        plan.HasMany(x => x.Assignees).WithOne(x => x.PatrolPlan).HasForeignKey(x => x.PatrolPlanId).OnDelete(DeleteBehavior.Cascade);
        plan.HasMany(x => x.Tasks).WithOne(x => x.PatrolPlan).HasForeignKey(x => x.PatrolPlanId).OnDelete(DeleteBehavior.Restrict);

        var assignee = modelBuilder.Entity<PatrolPlanAssignee>();
        assignee.HasKey(x => x.Id);
        assignee.Property(x => x.AssigneeKey).HasMaxLength(100).IsRequired();
        assignee.HasIndex(x => new { x.PatrolPlanId, x.AssigneeKey }).IsUnique();

        var task = modelBuilder.Entity<PatrolTask>();
        task.HasKey(x => x.Id);
        task.Property(x => x.TaskNo).HasMaxLength(50).IsRequired();
        task.HasIndex(x => x.TaskNo).IsUnique();
        task.HasIndex(x => new { x.PatrolPlanId, x.ScheduledOccurrenceUtc }).IsUnique();
        task.Property(x => x.AssignedInspectorKey).HasMaxLength(100).IsRequired();
        task.Property(x => x.Status).HasMaxLength(50).IsRequired();
        task.Property(x => x.GenerationSource).HasMaxLength(50).IsRequired();
        task.HasOne(x => x.PatrolStandard).WithMany().HasForeignKey(x => x.PatrolStandardId).OnDelete(DeleteBehavior.Restrict);
    }
}
