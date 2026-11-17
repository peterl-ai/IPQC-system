using System.Text.Json;
using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonConverter(typeof(BasicInformationFieldConverter))]
public enum BasicInformationField { Customer, Production, ProductMo }
public sealed class BasicInformationFieldConverter : JsonConverter<BasicInformationField>
{
    public override BasicInformationField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() switch {
            "Customer" => BasicInformationField.Customer, "Production" => BasicInformationField.Production,
            "ProductMo" => BasicInformationField.ProductMo, _ => throw new JsonException("Unknown Basic Information field.")
        } : throw new JsonException("A named Basic Information field is required.");
    public override void Write(Utf8JsonWriter writer, BasicInformationField value, JsonSerializerOptions options) {
        if (!Enum.IsDefined(value)) throw new JsonException("Unknown Basic Information field.");
        writer.WriteStringValue(value.ToString());
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record BasicInformationOperationInput(Guid OperationId, int CanonicalizationVersion, string Kind,
    [property: JsonRequired] BasicInformationField Field, Guid SourceWorkContextId, Guid ContributorUserIdAssertion,
    [property: JsonRequired] long BaseDraftVersion, [property: JsonRequired] long OriginLocalEditVersion,
    [property: JsonRequired] string? Value, DraftOperationReference? Predecessor);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record BasicInformationBatchInput(Guid BatchId, int CanonicalizationVersion, Guid ExpectedTaskLineId,
    int ExpectedWorkflowVersion, string ExpectedEnvironmentId, [property: JsonRequired] long ExpectedDraftVersion,
    BasicInformationOperationInput Operation, DraftSourceProof? SourceProof);
public sealed record BasicInformationReceipt(Guid TaskId, Guid OperationId, Guid BatchId, int CanonicalizationVersion,
    string ContentHash, string Kind, BasicInformationField Field, string? Value, Guid SourceWorkContextId,
    Guid ContributorUserId, Guid ActorUserId, Guid ActorPersonalSessionId, string EnvironmentId, Guid LineId,
    int WorkflowVersion, long BaseDraftVersion, long OriginLocalEditVersion, DateTime AcceptedAtUtc,
    long DraftVersion, DraftOperationReference? Predecessor);
public sealed record BasicInformationBatchResult(DraftBatchReceipt? BatchReceipt, BasicInformationReceipt Operation,
    bool ReplayOnly, bool CanExecute = false, bool CanSubmit = false, bool AcceptsFormalInspection = false);
public sealed record BasicInformationValues(string? Customer, string? Production, string? ProductMo);
public sealed record BasicInformationFieldOutput(BasicInformationField Field, string? Value, string ValueState,
    bool HasAcceptedFact, BasicInformationReceipt? LastOperation, bool IsRequired = false, bool BlocksSubmit = false);
public sealed record BasicInformationOutput(Guid TaskId, Guid LineId, int WorkflowVersion, long DraftVersion,
    string Availability, BasicInformationValues Values, List<BasicInformationFieldOutput> Fields,
    int AcceptedFieldCount, int NonblankFieldCount, List<BasicInformationReceipt> History, long? HistoryBeforeDraftVersion,
    bool CanExecute = false, bool CanSubmit = false, bool AcceptsFormalInspection = false,
    string Completeness = "OptionalHeaderFactsOnly", string Observation = "ReadOnlyPointInTime");
