using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class ProductionLineEndpoints
{
    public static void MapProductionLines(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/production-lines").RequireAuthorization(IpqcPermissions.LinesRead);
        group.AddEndpointFilter(ProvisioningError);
        group.MapGet("", async (bool? active, int? page, int? pageSize, ProductionLineService service, CancellationToken ct) =>
        {
            var (number, size) = Pagination(page, pageSize);
            return Results.Ok(await service.ListAsync(active, number, size, ct));
        });
        group.MapGet("/{id:guid}", async (Guid id, ProductionLineService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } row ? Results.Ok(row) : Results.NotFound());
        group.MapGet("/{id:guid}/audit", async (Guid id, int? page, int? pageSize, ProductionLineService service, ProvisioningStore store, CancellationToken ct) =>
        {
            var (number, size) = Pagination(page, pageSize);
            if (await service.GetAsync(id, ct) is null) return Results.NotFound();
            return Results.Ok(await store.AuditAsync(id, false, number, size, ct));
        });
        group.MapPost("", async (LineCreateInput input, ProductionLineService service, HttpContext http, CancellationToken ct) =>
        {
            var row = await service.CreateAsync(input, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct);
            return Results.Created($"/api/admin/production-lines/{row.Id}", row);
        }).RequireAuthorization(IpqcPermissions.LinesWrite);
        group.MapPut("/{id:guid}", async (Guid id, LineRenameInput input, ProductionLineService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.ChangeAsync(id, input.Name, null, input.ExpectedRevision, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
            .RequireAuthorization(IpqcPermissions.LinesWrite);
        foreach (var enabled in new[] { true, false })
            group.MapPost(enabled ? "/{id:guid}/activate" : "/{id:guid}/deactivate", async (Guid id, ProvisioningRevisionInput input,
                ProductionLineService service, HttpContext http, CancellationToken ct) => Results.Ok(await service.ChangeAsync(id, null, enabled,
                    input.ExpectedRevision, IpqcPermissions.UserId(http.User), IpqcPermissions.Username(http.User), ct)))
                .RequireAuthorization(IpqcPermissions.LinesWrite);
    }
    internal static (int Page, int Size) Pagination(int? page, int? size)
    {
        var number = page ?? 1; var count = size ?? 20;
        if (number < 1 || count is < 1 or > 100 || (long)(number - 1) * count > int.MaxValue)
            throw new ProvisioningException("InvalidInput", 400);
        return (number, count);
    }
    internal static async ValueTask<object?> ProvisioningError(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (ProvisioningException ex) { return Results.Problem(statusCode: ex.Status, title: ex.Message,
            extensions: new Dictionary<string, object?> { ["code"] = ex.Message }); }
    }
}
