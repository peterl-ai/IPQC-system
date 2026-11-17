using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record PicCreateInput(Guid RequestId, [property: JsonRequired] string? Name,
    [property: JsonRequired] string? Department, [property: JsonRequired] bool IsActive);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record PicEditInput(Guid RequestId, [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string? Name, [property: JsonRequired] string? Department);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record PicRevisionInput(Guid RequestId, [property: JsonRequired] long ExpectedRevision);
public sealed record PicOutput(Guid PicId, long Revision, string Name, string Department, bool IsActive);
public sealed record PicRevisionReceipt(Guid PicId, long Revision, Guid RequestId, int ContractVersion,
    string Action, string Name, string Department, bool IsActive, Guid ActorUserId,
    Guid ActorPersonalSessionId, string ActorUsername, string EnvironmentId, DateTime OccurredAtUtc);
public sealed record PicMutationResult(PicRevisionReceipt Receipt, bool ReplayOnly);
public sealed record PicPage<T>(List<T> Items, int Page, int PageSize, bool HasMore);
public sealed record PicLookupOutput(Guid TaskId, Guid LineId, int WorkflowVersion, PicPage<PicOutput> Roster,
    bool CanExecute = false, bool CanSubmit = false, bool SelectionImplemented = false,
    string Observation = "ReadOnlyPointInTime");
