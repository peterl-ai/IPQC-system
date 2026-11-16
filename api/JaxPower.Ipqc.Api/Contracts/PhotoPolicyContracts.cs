using System.Text.Json.Serialization;

namespace JaxPower.Ipqc.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record PhotoPolicyConfirmInput(Guid RequestId, int CanonicalizationVersion, Guid? ExpectedDefinitionId,
    string ExpectedDefinitionHash, Guid? ExpectedConfirmationId, string Value);
public sealed record PhotoPolicyReceipt(Guid ConfirmationId, Guid RequestId, Guid StandardId, Guid StandardItemId,
    Guid DefinitionId, long DefinitionVersion, int CanonicalizationVersion, string DefinitionHash, long PolicyVersion,
    string Value, Guid ConfirmedByUserId, Guid ConfirmedPersonalSessionId, string EnvironmentId, DateTime ConfirmedAtUtc);
public sealed record PhotoPolicyItem(Guid StandardId, Guid StandardItemId, string RawPhotoRequirement,
    Guid? DefinitionId, long? DefinitionVersion, string DefinitionHash, string State, PhotoPolicyReceipt? Confirmation);
public sealed record PhotoPolicyPage(Guid StandardId, List<PhotoPolicyItem> Items, StandardOutput? ReviewStandard = null);
public sealed record FrozenPhotoPolicy(Guid TaskId, Guid TaskItemId, Guid PlanId, long SourcePlanRevision, PhotoPolicyReceipt Policy, int ContractVersion = 1);
public sealed record TaskPhotoPolicyItem(Guid TaskItemId, string State, FrozenPhotoPolicy? FrozenPolicy);
public sealed record TaskPhotoPolicies(Guid TaskId, List<TaskPhotoPolicyItem> Items, bool CanExecute = false);
