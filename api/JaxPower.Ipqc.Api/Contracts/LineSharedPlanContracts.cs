using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SharedPlanInput(string? PlanNo, string PlanName, Guid PatrolStandardId, Guid LineId,
    List<string> Rounds, string EffectiveStartLocal, string? EffectiveEndLocal, bool IsEnabled,
    long? ExpectedRevision = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SharedPlanRevisionInput(long ExpectedRevision);
public sealed record SharedPlanOutput(Guid Id, string PlanNo, string PlanName, Guid PatrolStandardId,
    Guid? LineId, List<string> Rounds, string EffectiveStartLocal, string? EffectiveEndLocal,
    bool IsEnabled, long? SharedPlanRevision, string ConfigurationStatus, DateTime GenerationNotBeforeUtc,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string UpdatedBy);
public sealed record SharedPlanPage(List<SharedPlanOutput> Items, int Page, int PageSize, int Total);
