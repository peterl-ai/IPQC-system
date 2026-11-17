using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class BasicInformationCanonical
{
    // Fixed V1 property order, explicit null/empty, unchanged Unicode, normalized IDs/hashes.
    // No proof, secret hash, current uploader credential/family or asserted physical capture time.
    public static string Operation(Guid task, BasicInformationBatchInput b) {
        var o = b.Operation;
        return JsonSerializer.Serialize(new { domain = "basic-information-field", v = o.CanonicalizationVersion,
            environment = b.ExpectedEnvironmentId, task = DraftCanonical.Key(task), line = DraftCanonical.Key(b.ExpectedTaskLineId),
            workflow = b.ExpectedWorkflowVersion, operation = DraftCanonical.Key(o.OperationId), kind = o.Kind, field = o.Field.ToString(),
            value = o.Value, source = DraftCanonical.Key(o.SourceWorkContextId), contributorAssertion = DraftCanonical.Key(o.ContributorUserIdAssertion),
            @base = o.BaseDraftVersion, local = o.OriginLocalEditVersion,
            predecessor = o.Predecessor is null ? null : new { operation = DraftCanonical.Key(o.Predecessor.OperationId), hash = o.Predecessor.ContentHash.ToUpperInvariant() }
        }, DraftCanonical.Json);
    }
    public static string Batch(Guid task, BasicInformationBatchInput b, string hash) => DraftCanonical.Hash(JsonSerializer.Serialize(new {
        domain = "basic-information-batch", v = b.CanonicalizationVersion, task = DraftCanonical.Key(task), batch = DraftCanonical.Key(b.BatchId),
        environment = b.ExpectedEnvironmentId, line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion,
        expected = b.ExpectedDraftVersion, operationHash = hash
    }, DraftCanonical.Json));
}
