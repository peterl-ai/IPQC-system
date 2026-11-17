using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class NgPicSelectionCanonical
{
    public static List<Guid> Sorted(IEnumerable<Guid> ids) => ids.OrderBy(DraftCanonical.Key, StringComparer.Ordinal).ToList();
    public static string Operation(Guid task, NgPicBatchInput b) {
        var o = b.Operation;
        return JsonSerializer.Serialize(new { domain = "ng-pic-selection", v = o.CanonicalizationVersion,
            environment = b.ExpectedEnvironmentId, task = DraftCanonical.Key(task), line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion,
            operation = DraftCanonical.Key(o.OperationId), kind = o.Kind, item = DraftCanonical.Key(o.TaskItemId), attempt = DraftCanonical.Key(o.InspectionAttemptId),
            source = DraftCanonical.Key(o.SourceWorkContextId), contributorAssertion = DraftCanonical.Key(o.ContributorUserIdAssertion),
            @base = o.BaseDraftVersion, local = o.OriginLocalEditVersion, picIds = Sorted(o.PicIds).Select(DraftCanonical.Key),
            predecessor = o.Predecessor is null ? null : new { operation = DraftCanonical.Key(o.Predecessor.OperationId), hash = o.Predecessor.ContentHash.ToUpperInvariant() }
        }, DraftCanonical.Json);
    }
    public static string Batch(Guid task, NgPicBatchInput b, string hash) => DraftCanonical.Hash(JsonSerializer.Serialize(new { domain = "ng-pic-selection-batch",
        v = b.CanonicalizationVersion, task = DraftCanonical.Key(task), batch = DraftCanonical.Key(b.BatchId), environment = b.ExpectedEnvironmentId,
        line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion, expected = b.ExpectedDraftVersion, operationHash = hash }, DraftCanonical.Json));
}
