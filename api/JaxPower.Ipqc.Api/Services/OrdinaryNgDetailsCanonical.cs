using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class OrdinaryNgDetailsCanonical
{
    public static string Operation(Guid task, OrdinaryNgDetailsBatchInput batch)
    {
        var op = batch.Operation;
        return JsonSerializer.Serialize(new {
            domain = "ordinary-ng-details", v = op.CanonicalizationVersion,
            environment = batch.ExpectedEnvironmentId, task = DraftCanonical.Key(task), line = DraftCanonical.Key(batch.ExpectedTaskLineId),
            workflow = batch.ExpectedWorkflowVersion, operation = DraftCanonical.Key(op.OperationId), kind = op.Kind,
            item = DraftCanonical.Key(op.TaskItemId), attempt = DraftCanonical.Key(op.InspectionAttemptId),
            source = DraftCanonical.Key(op.SourceWorkContextId), contributorAssertion = DraftCanonical.Key(op.ContributorUserIdAssertion),
            @base = op.BaseDraftVersion, local = op.OriginLocalEditVersion, op.NgType, op.NgReason, op.Remarks,
            predecessor = op.Predecessor is null ? null : new {
                operation = DraftCanonical.Key(op.Predecessor.OperationId), hash = op.Predecessor.ContentHash.ToUpperInvariant()
            }
        }, DraftCanonical.Json);
    }

    public static string Batch(Guid task, OrdinaryNgDetailsBatchInput batch, string hash) =>
        DraftCanonical.Hash(JsonSerializer.Serialize(new {
            domain = "ordinary-ng-details-batch", v = batch.CanonicalizationVersion,
            task = DraftCanonical.Key(task), batch = DraftCanonical.Key(batch.BatchId), environment = batch.ExpectedEnvironmentId,
            line = DraftCanonical.Key(batch.ExpectedTaskLineId), workflow = batch.ExpectedWorkflowVersion,
            expected = batch.ExpectedDraftVersion, operationHash = hash
        }, DraftCanonical.Json));
}
