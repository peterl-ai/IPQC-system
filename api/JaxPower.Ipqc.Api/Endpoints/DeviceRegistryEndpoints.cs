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
        group.MapPost("", async (DeviceCreateInput input, DeviceRegistryService service, HttpContext http, CancellationToken ct) =>
        {
            var row = await service.CreateAsync(input, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct);
            return Results.Created($"/api/admin/devices/{row.Id}", row);
        }).RequireAuthorization(IpqcPermissions.DevicesWrite);
        group.MapPut("/{id:guid}", async (Guid id, DeviceUpdateInput input, DeviceRegistryService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, input, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
            .RequireAuthorization(IpqcPermissions.DevicesWrite);
        foreach (var rebind in new[] { false, true })
            group.MapPost(rebind ? "/{id:guid}/rebind" : "/{id:guid}/bind", async (Guid id, DeviceBindingInput input,
                DeviceRegistryService service, HttpContext http, CancellationToken ct) => Results.Ok(await service.ReserveAsync(id, input, rebind,
                    IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
                .RequireAuthorization(IpqcPermissions.DevicesWrite);
        foreach (var revoke in new[] { false, true })
            group.MapPost(revoke ? "/{id:guid}/revoke" : "/{id:guid}/unbind", async (Guid id, DeviceReasonInput input,
                DeviceRegistryService service, HttpContext http, CancellationToken ct) => Results.Ok(await service.ReleaseAsync(id, input, revoke,
                    IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
                .RequireAuthorization(IpqcPermissions.DevicesWrite);
        group.MapPost("/{id:guid}/replace", async (Guid id, DeviceReplaceInput input, DeviceRegistryService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.ReplaceAsync(id, input, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
            .RequireAuthorization(IpqcPermissions.DevicesWrite);
    }
}
