using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class DeviceRegistryEndpoints
{
    public static void MapDeviceRegistry(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/devices").RequireAuthorization(IpqcPermissions.DevicesRead);
        group.AddEndpointFilter(ProductionLineEndpoints.ProvisioningError);
        group.MapGet("", async (Guid? lineId, int? page, int? pageSize, DeviceRegistryService service, CancellationToken ct) =>
        {
            var (number, size) = ProductionLineEndpoints.Pagination(page, pageSize);
            return Results.Ok(await service.ListAsync(lineId, number, size, ct));
        });
        group.MapGet("/{id:guid}", async (Guid id, DeviceRegistryService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } row ? Results.Ok(row) : Results.NotFound());
        group.MapGet("/{id:guid}/audit", async (Guid id, int? page, int? pageSize, DeviceRegistryService service, ProvisioningStore store, CancellationToken ct) =>
        {
            var (number, size) = ProductionLineEndpoints.Pagination(page, pageSize);
            if (await service.GetAsync(id, ct) is null) return Results.NotFound();
            return Results.Ok(await store.AuditAsync(id, true, number, size, ct));
        });
        group.MapPost("", DeviceProvisioningRetirement.Response).RequireAuthorization(IpqcPermissions.DevicesWrite);
        group.MapPut("/{id:guid}", DeviceProvisioningRetirement.Response).RequireAuthorization(IpqcPermissions.DevicesWrite);
        foreach (var action in new[] { "bind", "rebind", "unbind", "replace" })
            group.MapPost("/{id:guid}/" + action, DeviceProvisioningRetirement.Response).RequireAuthorization(IpqcPermissions.DevicesWrite);
        group.MapPost("/{id:guid}/revoke", async (Guid id, DeviceReasonInput input, DeviceRegistryService service,
            HttpContext http, CancellationToken ct) => Results.Ok(await service.ReleaseAsync(id, input, true,
                IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
            .RequireAuthorization(IpqcPermissions.DevicesWrite);
    }
}
