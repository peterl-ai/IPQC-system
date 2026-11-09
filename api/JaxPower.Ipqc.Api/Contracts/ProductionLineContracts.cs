using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LineCreateInput(string? Code, string? Name, bool IsActive = true);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LineRenameInput(string? Name, long ExpectedRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProvisioningRevisionInput(long ExpectedRevision);
public sealed record LineOutput(Guid Id, string Code, string Name, bool IsActive, long Revision);
public sealed record ProvisioningPage<T>(List<T> Items, int Page, int PageSize, int Total);
