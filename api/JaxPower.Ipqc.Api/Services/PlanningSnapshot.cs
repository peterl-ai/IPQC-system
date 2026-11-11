using System.Globalization;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;

namespace JaxPower.Ipqc.Api.Services;

public static class PlanningSnapshot
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static LineTaskDefinition Definition(PatrolTaskItem x) => new(x.Id, x.SourcePatrolStandardItemId, x.SequenceNo,
        x.ProcessCode, x.ProcessName, x.InspectionItemCategory, x.InspectionItem, x.InspectionContent,
        x.UpperLimitOperator, x.UpperLimitValue, x.LowerLimitOperator, x.LowerLimitValue, x.InspectionType,
        x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel);
    public static PlanningDefinitionV1 Freeze(PatrolTask t, PatrolPlan p, PatrolStandard s, ProductionLine line)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(t.ScheduledOccurrenceUtc, LineSharedPlanService.Zone);
        return new("PlanningDefinitionV1", t.Id, p.Id, p.SharedPlanRevision!.Value, p.PlanNo, p.PlanName,
            line.Id, line.Code, line.Name, line.Revision, 2, t.ScheduledOccurrenceUtc,
            local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.Hour == 7 ? "Day" : "Night",
            PatrolSchedule.PlantTimeZone, (int)LineSharedPlanService.Zone.GetUtcOffset(t.ScheduledOccurrenceUtc).TotalMinutes,
            p.ScheduleDefinition, Utc(p.EffectiveStartUtc), p.EffectiveEndUtc is { } end ? Utc(end) : null,
            Utc(p.GenerationNotBeforeUtc), s.Id, Utc(s.UpdatedAtUtc), s.PatrolStandardName,
            s.FactoryCode, s.FactoryName, s.WorkshopCode, s.MaterialCode, t.Items.OrderBy(x => x.SequenceNo).Select(Definition).ToList());
    }
    public static string Serialize(PlanningDefinitionV1 snapshot) => JsonSerializer.Serialize(snapshot, Json);
    public static string Status(PatrolTask task) => task.PlanningSnapshotVersion is null && task.PlanningSnapshotJson is null
        ? "HistoricalUnadmitted" : Read(task) is null ? "Invalid" : "ValidV1";
    public static PlanningDefinitionV1? Read(PatrolTask t)
    {
        if (t.PlanningSnapshotVersion != 1 || string.IsNullOrWhiteSpace(t.PlanningSnapshotJson)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<PlanningDefinitionV1>(t.PlanningSnapshotJson, Json);
            if (s is null || s.Kind != "PlanningDefinitionV1" || s.TaskId != t.Id || s.PlanId != t.PatrolPlanId ||
                s.LineId != t.LineId || s.WorkflowVersion != 2 || t.WorkflowVersion != WorkflowVersion.LineSharedV1 ||
                s.StandardId != t.PatrolStandardId || s.SourcePlanRevision < 1 || s.LineRevision < 1 ||
                s.ScheduledOccurrenceUtc != t.ScheduledOccurrenceUtc || s.TimeZoneId != PatrolSchedule.PlantTimeZone ||
                s.Round is not ("Day" or "Night") || s.UtcOffsetMinutes is < -840 or > 840 ||
                t.AssignedInspectorUserId is not null || t.AssignedInspectorKey != "" ||
                s.PlanNo != t.PlanNoSnapshot || s.PlanName != t.PlanNameSnapshot || s.StandardName != t.StandardNameSnapshot ||
                s.LineCode != t.LineCodeSnapshot || s.LineName != t.LineNameSnapshot ||
                s.FactoryCode != t.FactoryCodeSnapshot || s.FactoryName != t.FactoryNameSnapshot ||
                s.WorkshopCode != t.WorkshopCodeSnapshot || s.MaterialCode != t.MaterialCodeSnapshot ||
                s.SourceStandardUpdatedAtUtc != t.SourceStandardUpdatedAtUtc || s.Items is null ||
                !s.Items.SequenceEqual(t.Items.OrderBy(x => x.SequenceNo).Select(Definition)) ||
                !LineSharedPlanService.Rounds(s.ScheduleDefinition).Contains(s.Round) ||
                s.ScheduledOccurrenceUtc < s.EffectiveStartUtc || s.EffectiveEndUtc is { } end && s.ScheduledOccurrenceUtc > end ||
                s.ScheduledOccurrenceUtc <= s.GenerationNotBeforeUtc ||
                !DateTime.TryParseExact(s.ScheduledLocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) return null;
            // Validate using the frozen offset, never today's mutable timezone database.
            var expected = date.AddHours(s.Round == "Day" ? 7 : 19).AddMinutes(-s.UtcOffsetMinutes);
            return expected == s.ScheduledOccurrenceUtc ? s : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or InvalidOperationException) { return null; }
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
