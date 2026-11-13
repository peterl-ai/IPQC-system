using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class DraftAttachmentCanonical
{
    // Fixed V1 field order and explicit null; operation set order is ordinal GUID text.
    // Photo identity/hash binds immutable server provenance; transport secrets/actor do not enter the hash.
    public static string Operation(Guid task, DraftAttachmentBatchInput b, DraftAttachmentOperationInput o) => JsonSerializer.Serialize(new {
        domain = "draft-attachment", v = o.CanonicalizationVersion, environment = b.ExpectedEnvironmentId,
        task = DraftCanonical.Key(task), line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion,
        operation = DraftCanonical.Key(o.OperationId), kind = o.Kind, item = DraftCanonical.Key(o.TaskItemId),
        attempt = DraftCanonical.Key(o.InspectionAttemptId), attachment = DraftCanonical.Key(o.AttachmentId),
        clientPhoto = DraftCanonical.Key(o.ClientPhotoId), sha256 = o.Sha256.ToUpperInvariant(),
        source = DraftCanonical.Key(o.SourceWorkContextId), contributorAssertion = DraftCanonical.Key(o.ContributorUserIdAssertion),
        @base = o.BaseDraftVersion, local = o.OriginLocalEditVersion,
        predecessor = o.Predecessor is null ? null : new { operation = DraftCanonical.Key(o.Predecessor.OperationId), hash = o.Predecessor.ContentHash.ToUpperInvariant() }
    }, DraftCanonical.Json);
    public static string Batch(Guid task, DraftAttachmentBatchInput b, Dictionary<Guid, string> hashes) => DraftCanonical.Hash(JsonSerializer.Serialize(new {
        domain = "draft-attachment-batch", v = b.CanonicalizationVersion, task = DraftCanonical.Key(task), batch = DraftCanonical.Key(b.BatchId),
        environment = b.ExpectedEnvironmentId, line = DraftCanonical.Key(b.ExpectedTaskLineId), workflow = b.ExpectedWorkflowVersion,
        expected = b.ExpectedDraftVersion, operations = hashes.OrderBy(x => DraftCanonical.Key(x.Key), StringComparer.Ordinal)
            .Select(x => new { operation = DraftCanonical.Key(x.Key), hash = x.Value }).ToArray()
    }, DraftCanonical.Json));
}
