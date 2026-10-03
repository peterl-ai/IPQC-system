namespace JaxPower.Ipqc.Api.Contracts;

public sealed record ItemInput(
    string? ProcessCode, string? ProcessName, string? InspectionItemCategory, string? InspectionItem,
    string? InspectionContent, string? UpperLimitOperator, string? UpperLimitValue,
    string? LowerLimitOperator, string? LowerLimitValue, string? InspectionType,
    string? SamplingPlan, string? SampleCount, string? PhotoRequirement, string? DefectLevel,
    Guid? Id = null);

public sealed record StandardInput(
    string? PatrolStandardName, string? FactoryCode, string? FactoryName, string? WorkshopCode,
    string? LineCode, string? LineName, string? MaterialCode, List<ItemInput>? InspectionItems);

public sealed record ItemOutput(
    Guid Id, int SequenceNo, string ProcessCode, string ProcessName, string InspectionItemCategory,
    string InspectionItem, string InspectionContent, string UpperLimitOperator, string UpperLimitValue,
    string LowerLimitOperator, string LowerLimitValue, string InspectionType, string SamplingPlan,
    string SampleCount, string PhotoRequirement, string DefectLevel);

public sealed record StandardOutput(
    Guid Id, string PatrolStandardName, string FactoryCode, string FactoryName, string WorkshopCode,
    string LineCode, string LineName, string MaterialCode, string CreatedBy, DateTime CreatedAtUtc,
    string UpdatedBy, DateTime UpdatedAtUtc, List<ItemOutput> InspectionItems);

public sealed record StandardSummary(
    Guid Id, string PatrolStandardName, string FactoryCode, string FactoryName, string WorkshopCode,
    string LineCode, string LineName, string MaterialCode, string CreatedBy, DateTime CreatedAtUtc,
    string UpdatedBy, DateTime UpdatedAtUtc);

public sealed record PagedStandards(List<StandardSummary> Items, int Page, int PageSize, int Total);
public sealed record CopyInput(string? PatrolStandardName);
