namespace JaxPower.Ipqc.Api.Contracts;

public sealed record CompletedRecordSummary(Guid Id, string TaskNo, DateTime? CompletedAtUtc,
    DateTime ScheduledOccurrenceUtc, DateTime? SubmittedAtUtc, string? OverallInspectionResult,
    int FinalRevisionNo, string Inspector, string? Shift, string? PlanNo, string? PlanName,
    string? StandardName, string? FactoryCode, string? FactoryName, string? WorkshopCode,
    string? LineCode, string? LineName, string? MaterialCode, string? ApprovedBy, DateTime? ApprovedAtUtc);
public sealed record PagedCompletedRecords(List<CompletedRecordSummary> Items, int Page, int PageSize, int Total);
public sealed record CompletedRecordSample(int SequenceNo, decimal? InspectionValue, string JudgmentResult,
    DateTime? InspectedAtUtc);
public sealed record CompletedRecordItem(int SequenceNo, string ProcessCode, string ProcessName,
    string InspectionItemCategory, string InspectionItem, string InspectionContent, string InspectionType,
    string LowerLimitOperator, string LowerLimitValue, string UpperLimitOperator, string UpperLimitValue,
    string SamplingPlan, string SampleCount, string PhotoRequirement, string DefectLevel,
    bool IsNa, string JudgmentResult, DateTime InspectedAtUtc, string? MachineCode, string? Series,
    string? Mold, string? AbnormalType, string? AbnormalCause, string? Remarks,
    List<CompletedRecordSample> Samples);
public sealed record CompletedRecordRevision(int RevisionNo, string Shift, string OverallInspectionResult,
    string SubmittedBy, DateTime SubmittedAtUtc, string? ReviewDecision, string? ReviewedBy,
    DateTime? ReviewedAtUtc, string? RejectReason, List<CompletedRecordRevisionItem> Items);
public sealed record CompletedRecordRevisionItem(int SequenceNo, bool IsNa, string JudgmentResult,
    DateTime InspectedAtUtc, List<CompletedRecordSample> Samples);
public sealed record CompletedPatrolReport(CompletedRecordSummary Summary, DateTime GeneratedAtUtc,
    DateTime? StartedAtUtc, int FinalRevisionNo, string FinalSubmittedBy, DateTime FinalSubmittedAtUtc,
    List<CompletedRecordItem> Items, List<CompletedRecordRevision> RevisionHistory);
