using JaxPower.Ipqc.Api.Domain;

namespace JaxPower.Ipqc.Api.Services;

internal static class InitialInspectionIdentity
{
    public static string State(PatrolTask task)
    {
        var frozen = PlanningSnapshot.Read(task);
        if (frozen is null || task.Items.Count == 0) return "UnsupportedSnapshot";
        if (frozen.Items.Any(x => !InspectionJudgmentService.TryRequiredSamples(x.SampleCount, out _, out _)))
            return "UnsupportedRequirements";
        if (task.Items.All(x => x.InspectionAttempts.Count == 0 && x.CurrentInspectionAttempt is null))
            return "Uninitialized";
        foreach (var item in task.Items)
        {
            if (item.InspectionAttempts.Count != 1 || item.CurrentInspectionAttempt is not { } current)
                return "UnsupportedIdentity";
            var a = item.InspectionAttempts[0];
            InspectionJudgmentService.TryRequiredSamples(item.SampleCount, out var minimum, out _);
            if (a.Id == Guid.Empty || a.Id == item.Id || a.Id == item.SourcePatrolStandardItemId ||
                a.Kind != "Initial" || a.PatrolTaskItemId != item.Id || a.PlanningSnapshotVersion != 1 ||
                a.FrozenSampleCount != item.SampleCount || a.MinimumSampleCount != minimum ||
                current.PatrolTaskItemId != item.Id || current.InspectionAttemptId != a.Id ||
                a.CreationEventId == Guid.Empty || a.CreatedAtUtc == default ||
                a.CreationSource is not ("TaskGeneration" or "ExplicitInitialization")) return "UnsupportedIdentity";
        }
        return "Initialized";
    }

    // Caller owns the existing planning or explicit-initialization transaction.
    // Does not alter Task status, timestamps, frozen definitions, answers or samples.
    public static IReadOnlyList<InspectionAttempt> Create(PatrolTask task, DateTime now, Guid? actor = null, string? username = null,
        Guid? family = null, string? reason = null)
    {
        if (State(task) != "Uninitialized" || LineSharedTaskEligibility.Availability(task) != "FoundationOnly")
            throw new InvalidOperationException("Task cannot establish initial inspection identities.");
        var creation = Guid.NewGuid();
        foreach (var item in task.Items)
        {
            InspectionJudgmentService.TryRequiredSamples(item.SampleCount, out var minimum, out _);
            var a = new InspectionAttempt { Id = Guid.NewGuid(), PatrolTaskItemId = item.Id,
                CreationEventId = creation, CreationSource = actor is null ? "TaskGeneration" : "ExplicitInitialization",
                CreatedAtUtc = now, CreatedByUserId = actor, CreatedByUsername = username,
                CreatedByPersonalSessionId = family, InitializationReason = reason,
                FrozenSampleCount = item.SampleCount, MinimumSampleCount = minimum };
            item.InspectionAttempts.Add(a);
            item.CurrentInspectionAttempt = new() { PatrolTaskItemId = item.Id, InspectionAttemptId = a.Id, InspectionAttempt = a };
        }
        return task.Items.SelectMany(x => x.InspectionAttempts).ToList();
    }
}
