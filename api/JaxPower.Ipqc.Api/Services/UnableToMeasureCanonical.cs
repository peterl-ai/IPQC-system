using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class UnableToMeasureCanonical
{
    // Fixed V1 field order, explicit nulls, exact Unicode, lowercase GUIDs and uppercase hashes.
    // The accepted reference hash seals photo provenance; no current credentials or raw proof.
    public static string Operation(Guid task, UnableToMeasureBatchInput b) {
        var o = b.Operation;
        object? Ref(DraftOperationReference? r) => r is null ? null : new { operation = DraftCanonical.Key(r.OperationId), hash = r.ContentHash.ToUpperInvariant() };
        return JsonSerializer.Serialize(new {
            domain = "unable-to-measure", v = o.CanonicalizationVersion, task = DraftCanonical.Key(task), environment = b.ExpectedEnvironmentId,
            line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion, operation = DraftCanonical.Key(o.OperationId), kind = o.Kind,
            item = DraftCanonical.Key(o.TaskItemId), attempt = DraftCanonical.Key(o.InspectionAttemptId), source = DraftCanonical.Key(o.SourceWorkContextId),
            contributorAssertion = DraftCanonical.Key(o.ContributorUserIdAssertion), @base = o.BaseDraftVersion, local = o.OriginLocalEditVersion,
            ngReason = o.NgReason, evidence = Ref(o.Evidence), predecessor = Ref(o.Predecessor)
        }, DraftCanonical.Json);
    }
    public static string Batch(Guid task, UnableToMeasureBatchInput b, string hash) => DraftCanonical.Hash(JsonSerializer.Serialize(new {
        domain = "unable-to-measure-batch", v = b.CanonicalizationVersion, task = DraftCanonical.Key(task), batch = DraftCanonical.Key(b.BatchId),
        line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion, environment = b.ExpectedEnvironmentId,
        expected = b.ExpectedDraftVersion, operationHash = hash
    }, DraftCanonical.Json));
}
