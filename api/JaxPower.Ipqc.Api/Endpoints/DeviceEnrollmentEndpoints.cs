using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.AspNetCore.Http.Features;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class DeviceEnrollmentEndpoints
{
    public static void MapDeviceEnrollment(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(IpqcPermissions.DevicesRead).RequireRateLimiting("enrollment-admin");
        admin.AddEndpointFilter(ProductionLineEndpoints.ProvisioningError);
        admin.AddEndpointFilter(NoStore);
        admin.MapGet("/devices/{id:guid}/enrollments", async (Guid id, int? page, int? pageSize, DeviceEnrollmentService service, CancellationToken ct) =>
        {
            var (number, size) = ProductionLineEndpoints.Pagination(page, pageSize);
            return Results.Ok(await service.ListAsync(id, number, size, ct));
        });
        admin.MapPost("/devices/{id:guid}/enrollments", DeviceProvisioningRetirement.Response)
            .RequireAuthorization(IpqcPermissions.DevicesWrite);
        admin.MapPost("/enrollments/{id:guid}/approve", DeviceProvisioningRetirement.Response)
            .RequireAuthorization(IpqcPermissions.DevicesWrite);
        admin.MapPost("/enrollments/{id:guid}/cancel", async (Guid id, EnrollmentCancelInput input, DeviceEnrollmentService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.CancelAsync(id, input, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
            .RequireAuthorization(IpqcPermissions.DevicesWrite);

        var bootstrap = app.MapGroup("/bootstrap/v1/enrollments").AllowAnonymous().RequireRateLimiting("enrollment-bootstrap");
        bootstrap.AddEndpointFilter(ProductionLineEndpoints.ProvisioningError);
        bootstrap.AddEndpointFilter(NoStore);
        foreach (var action in new[] { "challenge", "proof", "receipt-challenge", "receipt" })
            bootstrap.MapPost("/{id:guid}/" + action, () => Results.NotFound());
    }
    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    { context.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(context); }

    public static bool IsEnrollmentPath(PathString path) => path.StartsWithSegments("/bootstrap/v1/enrollments") ||
        path.StartsWithSegments("/api/admin/enrollments") ||
        (path.StartsWithSegments("/api/admin/devices") && path.Value!.EndsWith("/enrollments", StringComparison.OrdinalIgnoreCase));
    public static void LimitEnrollmentBody(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = 32768;
    }
}
