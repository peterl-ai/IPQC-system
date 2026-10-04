namespace JaxPower.Ipqc.Api.Contracts;

public sealed record TaskSampleDraft(decimal? InspectionValue, string? JudgmentResult);
public sealed record TaskItemDraft(Guid Id, bool? IsNa, List<TaskSampleDraft>? Samples,
    string? MachineCode = null, string? Series = null, string? Mold = null,
    string? AbnormalType = null, string? AbnormalCause = null, string? Remarks = null);
public sealed record TaskDraftInput(string? Shift, List<TaskItemDraft>? Items);
public sealed record RejectTaskInput(string? Reason);
public sealed record BatchApproveInput(List<Guid>? TaskIds);

public sealed record TaskSummary(Guid Id, string TaskNo, string Status, DateTime ScheduledOccurrenceUtc,
    DateTime GeneratedAtUtc, DateTime? SubmittedAtUtc, string? OverallInspectionResult, string AssignedInspectorKey,
    string? PlanNoSnapshot, string? PlanNameSnapshot, string? StandardNameSnapshot, string? FactoryNameSnapshot,
    string? LineNameSnapshot, string? Shift, int CurrentRevisionNo);
public sealed record PagedTasks(List<TaskSummary> Items, int Page, int PageSize, int Total);
public sealed record TaskSampleOutput(Guid Id, int SequenceNo, decimal? InspectionValue,
    string? JudgmentResult, DateTime? InspectedAtUtc);
public sealed record TaskItemOutput(Guid Id, Guid SourcePatrolStandardItemId, int SequenceNo, string ProcessCode,
    string ProcessName, string InspectionItemCategory, string InspectionItem, string InspectionContent,
    string UpperLimitOperator, string UpperLimitValue, string LowerLimitOperator, string LowerLimitValue,
    string InspectionType, string SamplingPlan, string SampleCount, string PhotoRequirement, string DefectLevel,
    bool? IsNa, string? JudgmentResult, string? MachineCode, string? Series, string? Mold,
    string? AbnormalType, string? AbnormalCause, string? Remarks, List<TaskSampleOutput> Samples);
public sealed record SubmissionSampleOutput(int SequenceNo, decimal? InspectionValue, string JudgmentResult, DateTime? InspectedAtUtc);
public sealed record SubmissionItemOutput(Guid PatrolTaskItemId, int SequenceNo, bool IsNa, string JudgmentResult,
    string? MachineCode, string? Series, string? Mold, string? AbnormalType, string? AbnormalCause,
    string? Remarks, List<SubmissionSampleOutput> Samples);
public sealed record ReviewOutput(int RevisionNo, string Reviewer, string Decision, string? Reason, DateTime ReviewedAtUtc);
public sealed record SubmissionOutput(int RevisionNo, string Shift, string OverallInspectionResult,
    string SubmittedBy, DateTime SubmittedAtUtc, List<SubmissionItemOutput> Items, ReviewOutput? Review);
public sealed record TaskDetail(TaskSummary Summary, string? FactoryCodeSnapshot, string? WorkshopCodeSnapshot,
    string? LineCodeSnapshot, string? MaterialCodeSnapshot, DateTime? SourceStandardUpdatedAtUtc,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc, List<TaskItemOutput> Items, List<SubmissionOutput> Submissions);
