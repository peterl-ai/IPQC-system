using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class PhotoPolicyEndpoints
{
    public static void MapPhotoPolicies(this WebApplication app)
    {
        PersonalAccess.RequireCurrent(app.MapGet("/api/patrol-standards/{standardId:guid}/photo-policies", async (Guid standardId,
            PhotoPolicyService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.ReadAsync(standardId, h.User, ct)))
            .RequireAuthorization(IpqcPermissions.StandardsRead), false);
        PersonalAccess.RequireCurrent(app.MapPost("/api/patrol-standards/{standardId:guid}/items/{itemId:guid}/photo-policy-confirmations",
            async (Guid standardId, Guid itemId, PhotoPolicyConfirmInput input, PhotoPolicyService s, HttpContext h, CancellationToken ct) =>
                Results.Ok(await s.ConfirmAsync(standardId, itemId, input, h.User, ct)))
            .RequireAuthorization(IpqcPermissions.PhotoPoliciesConfirm).WithMetadata(new SharedDraftMutation()), false);
        PersonalAccess.RequireCurrent(app.MapGet("/api/photo-policy-confirmations/{id:guid}", async (Guid id,
            PhotoPolicyService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.ReadReceiptAsync(id, h.User, ct)))
            .RequireAuthorization(IpqcPermissions.StandardsRead), false);
    }
}
