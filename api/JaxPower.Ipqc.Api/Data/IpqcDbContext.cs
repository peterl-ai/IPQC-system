using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext(DbContextOptions<IpqcDbContext> options) : DbContext(options)
{
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<RegisteredDevice> RegisteredDevices => Set<RegisteredDevice>();
    public DbSet<ProvisioningAuditEvent> ProvisioningAuditEvents => Set<ProvisioningAuditEvent>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();
    public DbSet<PatrolStandard> PatrolStandards => Set<PatrolStandard>();
    public DbSet<PatrolStandardItem> PatrolStandardItems => Set<PatrolStandardItem>();
    public DbSet<PatrolPlan> PatrolPlans => Set<PatrolPlan>();
    public DbSet<PatrolPlanAssignee> PatrolPlanAssignees => Set<PatrolPlanAssignee>();
    public DbSet<PatrolTask> PatrolTasks => Set<PatrolTask>();
    public DbSet<PatrolTaskItem> PatrolTaskItems => Set<PatrolTaskItem>();
    public DbSet<PatrolTaskItemSample> PatrolTaskItemSamples => Set<PatrolTaskItemSample>();
    public DbSet<PatrolTaskSubmission> PatrolTaskSubmissions => Set<PatrolTaskSubmission>();
    public DbSet<PatrolTaskSubmissionItem> PatrolTaskSubmissionItems => Set<PatrolTaskSubmissionItem>();
    public DbSet<PatrolTaskSubmissionSample> PatrolTaskSubmissionSamples => Set<PatrolTaskSubmissionSample>();
    public DbSet<PatrolTaskReview> PatrolTaskReviews => Set<PatrolTaskReview>();

    private void EnsureAppendOnlyAudit()
    {
        GuardWorkContexts();
        GuardInspectionAttempts();
        GuardSharedDrafts();
        GuardItemRemovals();
        GuardAttachments();
        if (ChangeTracker.Entries<ProvisioningAuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Provisioning audit events are append-only.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    { EnsureAppendOnlyAudit(); return base.SaveChanges(acceptAllChangesOnSuccess); }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { EnsureAppendOnlyAudit(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureEnrollment(modelBuilder);
        ConfigureWorkContexts(modelBuilder);
        ConfigureInspectionAttempts(modelBuilder);
        ConfigureSharedDrafts(modelBuilder);
        ConfigureItemRemovals(modelBuilder);
        ConfigureAttachments(modelBuilder);
        var line = modelBuilder.Entity<ProductionLine>();
        line.HasKey(x => x.Id);
        line.Property(x => x.Revision).HasDefaultValue(1L).HasSentinel(1L).IsConcurrencyToken();
        line.ToTable(t => t.HasCheckConstraint("CK_ProductionLines_Revision", "\"Revision\" >= 1"));
        line.Property(x => x.Code).HasMaxLength(100).IsRequired().UsePropertyAccessMode(PropertyAccessMode.Property);
        line.Property(x => x.NormalizedCode).HasMaxLength(100).IsRequired();
        line.Property(x => x.Name).HasMaxLength(200).IsRequired();
        line.HasIndex(x => x.NormalizedCode).IsUnique();
        line.ToTable(t => t.HasCheckConstraint("CK_ProductionLines_Code", "length(trim(\"Code\")) > 0 AND length(trim(\"NormalizedCode\")) > 0"));
        var device = modelBuilder.Entity<RegisteredDevice>();
        device.HasKey(x => x.Id);
        device.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        device.Property(x => x.Model).HasMaxLength(200);
        device.Property(x => x.Serial).HasMaxLength(200);
        device.Property(x => x.TerminalReason).HasMaxLength(2000);
        device.Property(x => x.Revision).IsConcurrencyToken();
        device.HasIndex(x => x.LineId).IsUnique();
        device.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        device.HasOne<RegisteredDevice>().WithMany().HasForeignKey(x => x.ReplacedByDeviceId).OnDelete(DeleteBehavior.Restrict);
        device.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        device.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        device.ToTable(t => t.HasCheckConstraint("CK_RegisteredDevices_Provisioning", "\"Status\" IN (1, 2, 3) AND \"Revision\" >= 1 AND \"BindingVersion\" >= 1 AND ((\"Status\" = 1 AND \"TerminalAtUtc\" IS NULL AND \"TerminalReason\" IS NULL) OR (\"Status\" IN (2, 3) AND \"LineId\" IS NULL AND \"TerminalAtUtc\" IS NOT NULL AND \"TerminalReason\" IS NOT NULL AND length(trim(\"TerminalReason\")) > 0)) AND ((\"Status\" = 3 AND \"ReplacedByDeviceId\" IS NOT NULL AND \"ReplacedByDeviceId\" <> \"Id\") OR (\"Status\" <> 3 AND \"ReplacedByDeviceId\" IS NULL))"));
        var audit = modelBuilder.Entity<ProvisioningAuditEvent>();
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Action).HasMaxLength(80).IsRequired();
        audit.Property(x => x.ActorUsername).HasMaxLength(100).IsRequired();
        audit.Property(x => x.Reason).HasMaxLength(2000);
        audit.HasIndex(x => new { x.DeviceId, x.OccurredAtUtc });
        audit.HasIndex(x => new { x.ProductionLineId, x.OccurredAtUtc });
        audit.HasIndex(x => x.ChangeId);
        audit.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.OldLineId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.NewLineId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<RegisteredDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        audit.ToTable(t => t.HasCheckConstraint("CK_ProvisioningAuditEvents_Aggregate", "(\"DeviceId\" IS NOT NULL AND \"ProductionLineId\" IS NULL) OR (\"DeviceId\" IS NULL AND \"ProductionLineId\" IS NOT NULL)"));
        var user = modelBuilder.Entity<ApplicationUser>();
        user.HasKey(x => x.Id);
        user.Property(x => x.Username).HasMaxLength(100).IsRequired();
        user.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
        user.HasIndex(x => x.NormalizedUsername).IsUnique();
        user.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        user.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        user.Property(x => x.Role).HasMaxLength(10).IsRequired();
        user.Property(x => x.CreatedBy).HasMaxLength(100);
        user.Property(x => x.UpdatedBy).HasMaxLength(100);
        var session = modelBuilder.Entity<RefreshTokenSession>();
        session.Property(x => x.PersonalSessionEnvironmentId).HasMaxLength(200);
        session.HasIndex(x => new { x.UserId, x.PersonalSessionId });
        session.HasIndex(x => x.PersonalSessionId).IsUnique()
            .HasFilter("\"PersonalSessionId\" IS NOT NULL AND \"RevokedAtUtc\" IS NULL");
        session.HasKey(x => x.Id);
        session.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        session.HasIndex(x => x.TokenHash).IsUnique();
        session.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        session.Property(x => x.UserAgent).HasMaxLength(500);
        session.HasOne(x => x.User).WithMany(x => x.RefreshSessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
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
        plan.Property(x => x.SharedPlanRevision).IsConcurrencyToken();
        plan.ToTable(t => t.HasCheckConstraint("CK_PatrolPlans_SharedAdmission", "\"SharedPlanRevision\" IS NULL OR (\"WorkflowVersion\" = 2 AND \"SharedPlanRevision\" >= 1)"));
        plan.Property(x => x.WorkflowVersion).HasDefaultValue(WorkflowVersion.LegacyPersonalV1).HasSentinel(WorkflowVersion.LegacyPersonalV1).IsConcurrencyToken();
        plan.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        plan.ToTable(t => t.HasCheckConstraint("CK_PatrolPlans_Workflow", "\"WorkflowVersion\" IN (1, 2) AND (\"WorkflowVersion\" = 1 OR \"LineId\" IS NOT NULL)"));
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
        assignee.HasIndex(x => new { x.PatrolPlanId, x.UserId }).IsUnique();
        assignee.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        var task = modelBuilder.Entity<PatrolTask>();
        task.HasIndex(x => x.LineId);
        task.ToTable(t => t.HasCheckConstraint("CK_PatrolTasks_PlanningSnapshot", "(\"PlanningSnapshotVersion\" IS NULL AND \"PlanningSnapshotJson\" IS NULL) OR (\"WorkflowVersion\" = 2 AND \"PlanningSnapshotVersion\" IS NOT NULL AND \"PlanningSnapshotVersion\" = 1 AND \"PlanningSnapshotJson\" IS NOT NULL AND length(\"PlanningSnapshotJson\") > 0 AND \"AssignedInspectorUserId\" IS NULL AND \"AssignedInspectorKey\" = '')"));
        task.HasIndex(x => new { x.LineId, x.ScheduledOccurrenceUtc }).IsUnique()
            .HasDatabaseName("IX_PatrolTasks_AdmittedLineOccurrence")
            .HasFilter("\"WorkflowVersion\" = 2 AND \"PlanningSnapshotVersion\" = 1");
        task.HasIndex(x => new { x.WorkflowVersion, x.LineId, x.ScheduledOccurrenceUtc, x.Id });
        task.Property(x => x.WorkflowVersion).HasDefaultValue(WorkflowVersion.LegacyPersonalV1).HasSentinel(WorkflowVersion.LegacyPersonalV1).IsConcurrencyToken();
        task.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        task.ToTable(t => t.HasCheckConstraint("CK_PatrolTasks_Workflow", "\"WorkflowVersion\" IN (1, 2) AND (\"WorkflowVersion\" = 1 OR \"LineId\" IS NOT NULL)"));
        task.HasKey(x => x.Id);
        task.Property(x => x.TaskNo).HasMaxLength(50).IsRequired();
        task.HasIndex(x => x.TaskNo).IsUnique();
        task.HasIndex(x => new { x.PatrolPlanId, x.ScheduledOccurrenceUtc }).IsUnique();
        task.Property(x => x.AssignedInspectorKey).HasMaxLength(100).IsRequired();
        task.HasIndex(x => x.AssignedInspectorUserId);
        task.HasOne(x => x.AssignedInspectorUser).WithMany().HasForeignKey(x => x.AssignedInspectorUserId).OnDelete(DeleteBehavior.Restrict);
        task.Property(x => x.Status).HasMaxLength(50).IsRequired();
        task.Property(x => x.GenerationSource).HasMaxLength(50).IsRequired();
        task.HasOne(x => x.PatrolStandard).WithMany().HasForeignKey(x => x.PatrolStandardId).OnDelete(DeleteBehavior.Restrict);
        task.Property(x => x.PlanNoSnapshot).HasMaxLength(50);
        task.Property(x => x.PlanNameSnapshot).HasMaxLength(200);
        task.Property(x => x.StandardNameSnapshot).HasMaxLength(200);
        task.Property(x => x.Shift).HasMaxLength(10);
        task.Property(x => x.OverallInspectionResult).HasMaxLength(20);
        task.HasIndex(x => new { x.Status, x.ScheduledOccurrenceUtc });
        task.HasIndex(x => new { x.Status, x.CompletedAtUtc });

        var taskItem = modelBuilder.Entity<PatrolTaskItem>();
        taskItem.HasKey(x => x.Id);
        taskItem.HasIndex(x => new { x.PatrolTaskId, x.SequenceNo }).IsUnique();
        taskItem.HasOne(x => x.PatrolTask).WithMany(x => x.Items).HasForeignKey(x => x.PatrolTaskId).OnDelete(DeleteBehavior.Cascade);
        foreach (var property in typeof(PatrolTaskItem).GetProperties().Where(p => p.PropertyType == typeof(string)))
            taskItem.Property(property.Name).HasMaxLength(2000);

        var sample = modelBuilder.Entity<PatrolTaskItemSample>();
        sample.HasKey(x => x.Id);
        sample.HasIndex(x => new { x.PatrolTaskItemId, x.SequenceNo }).IsUnique();
        sample.HasOne(x => x.PatrolTaskItem).WithMany(x => x.Samples).HasForeignKey(x => x.PatrolTaskItemId).OnDelete(DeleteBehavior.Cascade);

        var submission = modelBuilder.Entity<PatrolTaskSubmission>();
        submission.HasKey(x => x.Id);
        submission.HasIndex(x => new { x.PatrolTaskId, x.RevisionNo }).IsUnique();
        submission.HasOne(x => x.PatrolTask).WithMany(x => x.Submissions).HasForeignKey(x => x.PatrolTaskId).OnDelete(DeleteBehavior.Cascade);
        submission.Property(x => x.Shift).HasMaxLength(10);
        submission.Property(x => x.OverallInspectionResult).HasMaxLength(20);
        submission.Property(x => x.SubmittedBy).HasMaxLength(100);

        var submissionItem = modelBuilder.Entity<PatrolTaskSubmissionItem>();
        submissionItem.HasKey(x => x.Id);
        submissionItem.HasIndex(x => new { x.PatrolTaskSubmissionId, x.SequenceNo }).IsUnique();
        // Preserve names already used by the shared historical migrations instead of
        // introducing Npgsql's different convention-based truncation into model diffs.
        submissionItem.HasOne(x => x.PatrolTaskSubmission).WithMany(x => x.Items).HasForeignKey(x => x.PatrolTaskSubmissionId)
            .HasConstraintName("FK_PatrolTaskSubmissionItems_PatrolTaskSubmissions_PatrolTaskSubmissionId").OnDelete(DeleteBehavior.Cascade);
        foreach (var property in typeof(PatrolTaskSubmissionItem).GetProperties().Where(p => p.PropertyType == typeof(string)))
            submissionItem.Property(property.Name).HasMaxLength(2000);

        var submissionSample = modelBuilder.Entity<PatrolTaskSubmissionSample>();
        submissionSample.HasKey(x => x.Id);
        submissionSample.HasIndex(x => new { x.PatrolTaskSubmissionItemId, x.SequenceNo })
            .HasDatabaseName("IX_PatrolTaskSubmissionSamples_PatrolTaskSubmissionItemId_SequenceNo").IsUnique();
        submissionSample.HasOne(x => x.PatrolTaskSubmissionItem).WithMany(x => x.Samples).HasForeignKey(x => x.PatrolTaskSubmissionItemId)
            .HasConstraintName("FK_PatrolTaskSubmissionSamples_PatrolTaskSubmissionItems_PatrolTaskSubmissionItemId").OnDelete(DeleteBehavior.Cascade);

        var review = modelBuilder.Entity<PatrolTaskReview>();
        review.HasKey(x => x.Id);
        review.HasIndex(x => new { x.PatrolTaskId, x.RevisionNo }).IsUnique();
        review.HasOne(x => x.PatrolTask).WithMany(x => x.Reviews).HasForeignKey(x => x.PatrolTaskId).OnDelete(DeleteBehavior.Cascade);
        review.Property(x => x.Reviewer).HasMaxLength(100);
        review.Property(x => x.Decision).HasMaxLength(20);
        review.Property(x => x.Reason).HasMaxLength(2000);
    }
}
