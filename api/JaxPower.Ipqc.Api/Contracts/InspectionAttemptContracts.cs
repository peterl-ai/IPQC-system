using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

public sealed record InitialInspectionAttemptOutput(Guid InspectionAttemptId, Guid TaskId, Guid TaskItemId,
    Guid LineId, int WorkflowVersion, string Kind, DateTime CreatedAtUtc, string CreationSource,
    int PlanningSnapshotVersion, int MinimumSampleCount, string FrozenSampleCount, bool IsCurrentApplicable);
public sealed record InspectionIdentityOutput(Guid TaskId, Guid LineId, int WorkflowVersion, string State,
    List<InitialInspectionAttemptOutput> Items, bool CanExecute = false);
public sealed record InspectionIdentityInventoryRow(Guid TaskId, Guid? LineId, int WorkflowVersion,
    string Category, int TaskItemCount, string? PlanningSnapshotHash);
public sealed record InspectionIdentityInventory(List<InspectionIdentityInventoryRow> Items, int Page, int PageSize, int Total);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InspectionIdentityInitializeInput(Guid ExpectedTaskLineId, int ExpectedWorkflowVersion,
    string ExpectedEnvironmentId, string ExpectedPlanningSnapshotHash, string Reason);
