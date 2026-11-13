using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public static class DraftCanonical
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static bool IsHash(string? text) => text is { Length: 64 } && text.All(char.IsAsciiHexDigit);
    public static string Key(Guid id) => id.ToString("D");
    private static string Encode(Action<Utf8JsonWriter> write) {
        using var bytes = new MemoryStream(); using (var w = new Utf8JsonWriter(bytes)) write(w);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    private static void References(Utf8JsonWriter w, IEnumerable<DraftOperationReference> refs) {
        w.WriteStartArray(); foreach (var r in refs.OrderBy(x=>Key(x.OperationId),StringComparer.Ordinal)) {
            w.WriteStartArray(); w.WriteStringValue(Key(r.OperationId)); w.WriteStringValue(r.ContentHash.ToUpperInvariant()); w.WriteEndArray();
        } w.WriteEndArray();
    }
    // V1 uses fixed field order, canonical GUIDs, invariant G29 decimals as strings,
    // explicit nulls, ordinal set order and unchanged Unicode (no normalization).
    // Transport proofs and current uploader credentials/family are intentionally absent.
    public static string Operation(Guid taskId, DraftBatchInput batch, DraftOperationInput op) => Encode(w=> {
        w.WriteStartObject(); w.WriteNumber("v",op.CanonicalizationVersion); w.WriteString("environment",batch.ExpectedEnvironmentId);
        w.WriteString("task",Key(taskId)); w.WriteString("line",Key(batch.ExpectedTaskLineId)); w.WriteNumber("workflow",batch.ExpectedWorkflowVersion);
        w.WriteString("operation",Key(op.OperationId)); w.WriteString("kind",op.Kind);
        w.WriteString("item",Key(op.TaskItemId)); w.WriteString("attempt",Key(op.InspectionAttemptId)); w.WriteString("clientSample",Key(op.ClientSampleId));
        w.WriteString("source",Key(op.SourceWorkContextId)); w.WriteString("contributorAssertion",Key(op.ContributorUserIdAssertion));
        w.WriteNumber("base",op.BaseDraftVersion); w.WriteNumber("local",op.OriginLocalEditVersion);
        w.WritePropertyName("data"); if (op.Data is null) w.WriteNullValue(); else {
            w.WriteStartObject(); w.WriteString("judgment",op.Data.Judgment);
            w.WriteString("value",op.Data.InspectionValue is { } n ? n == 0 ? "0" : n.ToString("G29",CultureInfo.InvariantCulture) : null);
            w.WriteString("barcode",op.Data.Barcode); w.WriteEndObject();
        }
        w.WritePropertyName("dependencies"); References(w,op.Dependencies); w.WriteEndObject();
    });
    public static string Batch(Guid taskId, DraftBatchInput b, IReadOnlyDictionary<Guid,string> operationHashes) => Hash(Encode(w=> {
        w.WriteStartObject(); w.WriteNumber("v",b.CanonicalizationVersion); w.WriteString("task",Key(taskId));
        w.WriteString("batch",Key(b.BatchId)); w.WriteString("environment",b.ExpectedEnvironmentId);
        w.WriteString("line",Key(b.ExpectedTaskLineId)); w.WriteNumber("workflow",b.ExpectedWorkflowVersion); w.WriteNumber("expected",b.ExpectedDraftVersion);
        w.WritePropertyName("operations"); References(w,operationHashes.Select(x=>new DraftOperationReference(x.Key,x.Value)));
        w.WritePropertyName("references"); References(w,b.AcceptedReferences); w.WriteEndObject();
    }));
}
