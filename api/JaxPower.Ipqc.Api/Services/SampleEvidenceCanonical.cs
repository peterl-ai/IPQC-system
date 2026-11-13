using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class SampleEvidenceCanonical
{
    public static string Operation(Guid task, SampleEvidenceBatchInput b) {
        var o = b.Operation;
        object? Ref(DraftOperationReference? r) => r is null ? null : new { operation = DraftCanonical.Key(r.OperationId), hash = r.ContentHash.ToUpperInvariant() };
        return JsonSerializer.Serialize(new { domain = "sample-evidence-binding", v = o.CanonicalizationVersion, task = DraftCanonical.Key(task),
            environment = b.ExpectedEnvironmentId, line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion,
            operation = DraftCanonical.Key(o.OperationId), binding = DraftCanonical.Key(o.BindingId), kind = o.Kind,
            item = DraftCanonical.Key(o.TaskItemId), attempt = DraftCanonical.Key(o.InspectionAttemptId), sampleOperation = Ref(o.SampleOperation),
            photoReference = Ref(o.PhotoReference), source = DraftCanonical.Key(o.SourceWorkContextId), contributorAssertion = DraftCanonical.Key(o.ContributorUserIdAssertion),
            @base = o.BaseDraftVersion, local = o.OriginLocalEditVersion, predecessor = Ref(o.Predecessor) }, DraftCanonical.Json);
    }
    public static string Batch(Guid task, SampleEvidenceBatchInput b, string hash) => DraftCanonical.Hash(JsonSerializer.Serialize(new {
        domain = "sample-evidence-binding-batch", v = b.CanonicalizationVersion, task = DraftCanonical.Key(task), batch = DraftCanonical.Key(b.BatchId),
        environment = b.ExpectedEnvironmentId, line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion,
        expected = b.ExpectedDraftVersion, operationHash = hash }, DraftCanonical.Json));
}
