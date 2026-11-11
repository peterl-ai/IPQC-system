namespace JaxPower.Ipqc.Api.Contracts;

// Planning metadata only. This is neither an execution contract nor an F2 canonical hash.
public sealed record PlanningDefinitionV1(string Kind, Guid TaskId, Guid PlanId, long SourcePlanRevision,
    string PlanNo, string PlanName, Guid LineId, string LineCode, string LineName, long LineRevision,
    int WorkflowVersion, DateTime ScheduledOccurrenceUtc, string ScheduledLocalDate, string Round,
    string TimeZoneId, int UtcOffsetMinutes, string ScheduleDefinition, DateTime EffectiveStartUtc,
    DateTime? EffectiveEndUtc, DateTime GenerationNotBeforeUtc, Guid StandardId,
    DateTime SourceStandardUpdatedAtUtc, string StandardName, string FactoryCode, string FactoryName,
    string WorkshopCode, string MaterialCode, List<LineTaskDefinition> Items);
