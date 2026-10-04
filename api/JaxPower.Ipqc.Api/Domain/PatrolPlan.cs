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
    public string? PlanNoSnapshot { get; set; }
    public string? PlanNameSnapshot { get; set; }
    public string? StandardNameSnapshot { get; set; }
    public string? FactoryCodeSnapshot { get; set; }
    public string? FactoryNameSnapshot { get; set; }
    public string? WorkshopCodeSnapshot { get; set; }
    public string? LineCodeSnapshot { get; set; }
    public string? LineNameSnapshot { get; set; }
    public string? MaterialCodeSnapshot { get; set; }
    public DateTime? SourceStandardUpdatedAtUtc { get; set; }
    public string? Shift { get; set; }
    public string? OverallInspectionResult { get; set; }
    public int CurrentRevisionNo { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<PatrolTaskItem> Items { get; set; } = [];
    public List<PatrolTaskSubmission> Submissions { get; set; } = [];
    public List<PatrolTaskReview> Reviews { get; set; } = [];
}

public sealed class PatrolTaskItem
{
    public Guid Id { get; set; }
    public Guid PatrolTaskId { get; set; }
    public PatrolTask PatrolTask { get; set; } = null!;
    public Guid SourcePatrolStandardItemId { get; set; }
    public int SequenceNo { get; set; }
    public string ProcessCode { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string InspectionItemCategory { get; set; } = "";
    public string InspectionItem { get; set; } = "";
    public string InspectionContent { get; set; } = "";
    public string UpperLimitOperator { get; set; } = "";
    public string UpperLimitValue { get; set; } = "";
    public string LowerLimitOperator { get; set; } = "";
    public string LowerLimitValue { get; set; } = "";
    public string InspectionType { get; set; } = "";
    public string SamplingPlan { get; set; } = "";
    public string SampleCount { get; set; } = "";
    public string PhotoRequirement { get; set; } = "";
    public string DefectLevel { get; set; } = "";
    public bool? IsNa { get; set; }
    public string? JudgmentResult { get; set; }
    public string? MachineCode { get; set; }
    public string? Series { get; set; }
    public string? Mold { get; set; }
    public string? AbnormalType { get; set; }
    public string? AbnormalCause { get; set; }
    public string? Remarks { get; set; }
    public List<PatrolTaskItemSample> Samples { get; set; } = [];
}

public sealed class PatrolTaskItemSample
{
    public Guid Id { get; set; }
    public Guid PatrolTaskItemId { get; set; }
    public PatrolTaskItem PatrolTaskItem { get; set; } = null!;
    public int SequenceNo { get; set; }
    public decimal? InspectionValue { get; set; }
    public string? JudgmentResult { get; set; }
    public DateTime? InspectedAtUtc { get; set; }
}

public sealed class PatrolTaskSubmission
{
    public Guid Id { get; set; }
    public Guid PatrolTaskId { get; set; }
    public PatrolTask PatrolTask { get; set; } = null!;
    public int RevisionNo { get; set; }
    public string Shift { get; set; } = "";
    public string OverallInspectionResult { get; set; } = "";
    public string SubmittedBy { get; set; } = "";
    public DateTime SubmittedAtUtc { get; set; }
    public List<PatrolTaskSubmissionItem> Items { get; set; } = [];
}

public sealed class PatrolTaskSubmissionItem
{
    public Guid Id { get; set; }
    public Guid PatrolTaskSubmissionId { get; set; }
    public PatrolTaskSubmission PatrolTaskSubmission { get; set; } = null!;
    public Guid PatrolTaskItemId { get; set; }
    public int SequenceNo { get; set; }
    public bool IsNa { get; set; }
    public string JudgmentResult { get; set; } = "";
    public string? MachineCode { get; set; }
    public string? Series { get; set; }
    public string? Mold { get; set; }
    public string? AbnormalType { get; set; }
    public string? AbnormalCause { get; set; }
    public string? Remarks { get; set; }
    public List<PatrolTaskSubmissionSample> Samples { get; set; } = [];
}

public sealed class PatrolTaskSubmissionSample
{
    public Guid Id { get; set; }
    public Guid PatrolTaskSubmissionItemId { get; set; }
    public PatrolTaskSubmissionItem PatrolTaskSubmissionItem { get; set; } = null!;
    public int SequenceNo { get; set; }
    public decimal? InspectionValue { get; set; }
    public string JudgmentResult { get; set; } = "";
    public DateTime? InspectedAtUtc { get; set; }
}

public sealed class PatrolTaskReview
{
    public Guid Id { get; set; }
    public Guid PatrolTaskId { get; set; }
    public PatrolTask PatrolTask { get; set; } = null!;
    public int RevisionNo { get; set; }
    public string Reviewer { get; set; } = "";
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
}
