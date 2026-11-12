using JaxPower.Ipqc.Api.Domain;
namespace JaxPower.Ipqc.Api.Services;
public static class LineSharedTaskEligibility
{
    public static string Availability(PatrolTask task)
    {
        if (PlanningSnapshot.Status(task) == "Invalid") return "UnsupportedSnapshot";
        if (task.Status is not ("PendingInspection" or "InProgress") || task.CurrentRevisionNo != 0 ||
            task.Submissions.Count != 0 || task.Reviews.Count != 0 || task.SubmittedAtUtc is not null ||
            task.CompletedAtUtc is not null || task.OverallInspectionResult is not null ||
            task.Items.Any(x => x.IsNa is not null || x.JudgmentResult is not null || x.InspectedAtUtc is not null ||
                x.MachineCode is not null || x.Series is not null || x.Mold is not null || x.AbnormalType is not null ||
                x.AbnormalCause is not null || x.Remarks is not null || x.Samples.Count != 0)) return "UnsupportedState";
        if (new[] { task.PlanNoSnapshot, task.PlanNameSnapshot, task.StandardNameSnapshot, task.LineNameSnapshot }
            .Any(string.IsNullOrWhiteSpace) || task.SourceStandardUpdatedAtUtc is null || task.Items.Count == 0 ||
            task.Items.Any(x => x.SourcePatrolStandardItemId == Guid.Empty || x.SequenceNo < 1) ||
            task.Items.Select(x => x.SequenceNo).Distinct().Count() != task.Items.Count ||
            task.Items.Select(x => x.SourcePatrolStandardItemId).Distinct().Count() != task.Items.Count) return "UnsupportedSnapshot";
        if (task.Items.Any(x => x.InspectionAttempts.Count != 0 || x.CurrentInspectionAttempt is not null) &&
            InitialInspectionIdentity.State(task) != "Initialized") return "UnsupportedState";
        return "FoundationOnly";
    }
}
