namespace JaxPower.Ipqc.Api.Domain;

// An empty, real work scope, never evidence that an inspection was performed.
// Task/line/workflow are reached through the restrictive TaskItem FK, not duplicated claims.
public sealed class InspectionAttempt
{
    public Guid Id { get; set; }
    public Guid PatrolTaskItemId { get; set; }
    public PatrolTaskItem PatrolTaskItem { get; set; } = null!;
    public string Kind { get; set; } = "Initial";
    public Guid CreationEventId { get; set; }
    public string CreationSource { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUsername { get; set; }
    public Guid? CreatedByPersonalSessionId { get; set; }
    public string? InitializationReason { get; set; }
    public int PlanningSnapshotVersion { get; set; } = 1;
    public string FrozenSampleCount { get; set; } = "";
    public int MinimumSampleCount { get; set; }
}

// Separate applicability permits future real reinspection history without rekeying TaskItems.
public sealed class InspectionAttemptApplicability
{
    public Guid PatrolTaskItemId { get; set; }
    public PatrolTaskItem PatrolTaskItem { get; set; } = null!;
    public Guid InspectionAttemptId { get; set; }
    public InspectionAttempt InspectionAttempt { get; set; } = null!;
}
