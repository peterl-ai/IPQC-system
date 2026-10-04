namespace JaxPower.Ipqc.Api.Domain;

public sealed class PatrolPlan
{
    public Guid Id { get; set; }
    public string PlanNo { get; set; } = "";
    public string PlanName { get; set; } = "";
    public Guid PatrolStandardId { get; set; }
    public PatrolStandard PatrolStandard { get; set; } = null!;
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
    public string LineCode { get; set; } = "";
    public string LineName { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime EffectiveStartUtc { get; set; }
    public DateTime? EffectiveEndUtc { get; set; }
    public string ScheduleDefinition { get; set; } = "";
    public string TimeZoneId { get; set; } = "America/New_York";
    public DateTime GenerationNotBeforeUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public List<PatrolPlanAssignee> Assignees { get; set; } = [];
    public List<PatrolTask> Tasks { get; set; } = [];
}

public sealed class PatrolPlanAssignee
{
    public Guid Id { get; set; }
    public Guid PatrolPlanId { get; set; }
    public PatrolPlan PatrolPlan { get; set; } = null!;
    public string AssigneeKey { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class PatrolTask
{
    public Guid Id { get; set; }
    public string TaskNo { get; set; } = "";
    public Guid PatrolPlanId { get; set; }
    public PatrolPlan PatrolPlan { get; set; } = null!;
    public Guid PatrolStandardId { get; set; }
    public PatrolStandard PatrolStandard { get; set; } = null!;
    public string AssignedInspectorKey { get; set; } = "";
    public DateTime ScheduledOccurrenceUtc { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string GenerationSource { get; set; } = "Scheduler";
    public string Status { get; set; } = "PendingInspection";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
