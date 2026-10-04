namespace JaxPower.Ipqc.Api.Contracts;

public sealed record ScheduleInput(string? Type, int? IntervalHours, List<string>? Times, List<string>? DaysOfWeek);
public sealed record PlanInput(string? PlanNo, string? PlanName, Guid PatrolStandardId,
    string? FactoryCode, string? FactoryName, string? LineCode, string? LineName,
    bool IsEnabled, string? EffectiveStartLocal, string? EffectiveEndLocal,
    string? TimeZoneId, ScheduleInput? Schedule, string? AssigneeKey);
public sealed record PlanSummary(Guid Id, string PlanNo, string PlanName, Guid PatrolStandardId,
    string PatrolStandardName, string FactoryCode, string FactoryName, string LineCode, string LineName,
    bool IsEnabled, string EffectiveStartLocal, string? EffectiveEndLocal, string TimeZoneId,
    ScheduleInput Schedule, string? AssigneeKey, DateTime UpdatedAtUtc);
public sealed record PlanOutput(Guid Id, string PlanNo, string PlanName, Guid PatrolStandardId,
    string PatrolStandardName, string FactoryCode, string FactoryName, string LineCode, string LineName,
    bool IsEnabled, string EffectiveStartLocal, string? EffectiveEndLocal, string TimeZoneId,
    ScheduleInput Schedule, string? AssigneeKey, DateTime GenerationNotBeforeUtc,
    string CreatedBy, DateTime CreatedAtUtc, string UpdatedBy, DateTime UpdatedAtUtc);
public sealed record PagedPlans(List<PlanSummary> Items, int Page, int PageSize, int Total);
public sealed record AssigneeOption(string Key, string DisplayName);
