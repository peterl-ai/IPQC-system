namespace JaxPower.Ipqc.Api.Domain;

public sealed class PatrolStandard
{
    public Guid Id { get; set; }
    public string? StandardNo { get; set; }
    public string PatrolStandardName { get; set; } = "";
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
    public string WorkshopCode { get; set; } = "";
    public string LineCode { get; set; } = "";
    public string LineName { get; set; } = "";
    public string MaterialCode { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public List<PatrolStandardItem> InspectionItems { get; set; } = [];
}

public sealed class PatrolStandardItem
{
    public Guid Id { get; set; }
    public Guid PatrolStandardId { get; set; }
    public PatrolStandard PatrolStandard { get; set; } = null!;
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
}
