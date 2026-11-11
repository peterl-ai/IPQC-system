namespace JaxPower.Ipqc.Api.Contracts;

public sealed record OfflineAccessReceipt(int Version, Guid UserId, Guid PersonalSessionId, string EnvironmentId,
    DateTime VerifiedAtUtc, DateTime OfflineAccessUntilUtc, DateTime PersonalSessionExpiresAtUtc);
public sealed record PersonalLine(Guid Id, string Code, string Name, int SelectionOrder);
public sealed record ValidatedTaskAccess(Guid UserId, Guid PersonalSessionId, string EnvironmentId,
    DateTime ValidatedAtUtc, Guid TaskId, Guid LineId, int WorkflowVersion);
public sealed record LineTaskSummary(Guid Id, string TaskNo, Guid LineId, int WorkflowVersion, string Status,
    DateTime ScheduledOccurrenceUtc, DateTime GeneratedAtUtc, int CurrentRevisionNo,
    string? PlanNoSnapshot, string? PlanNameSnapshot, string? StandardNameSnapshot, string? LineNameSnapshot,
    string ReadFoundationStatus, bool CanExecute, ValidatedTaskAccess ValidatedAccess, int? PlanningSnapshotVersion = null, long? SourcePlanRevision = null,
    string? RoundSnapshot = null, string? ScheduledLocalDateSnapshot = null, string PlanningSnapshotStatus = "HistoricalUnadmitted");
public sealed record PagedLineTasks(List<LineTaskSummary> Items, int Page, int PageSize, int Total);
public sealed record LineTaskDefinition(Guid Id, Guid SourcePatrolStandardItemId, int SequenceNo,
    string ProcessCode, string ProcessName, string InspectionItemCategory, string InspectionItem,
    string InspectionContent, string UpperLimitOperator, string UpperLimitValue, string LowerLimitOperator,
    string LowerLimitValue, string InspectionType, string SamplingPlan, string SampleCount,
    string PhotoRequirement, string DefectLevel);
public sealed record LineTaskDetail(LineTaskSummary Summary, Guid PatrolPlanId, Guid PatrolStandardId,
    string? FactoryCodeSnapshot, string? FactoryNameSnapshot, string? WorkshopCodeSnapshot,
    string? LineCodeSnapshot, string? MaterialCodeSnapshot, DateTime SourceStandardUpdatedAtUtc,
    List<LineTaskDefinition> Items, bool CanExecute = false, PlanningDefinitionV1? PlanningSnapshot = null);
