using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class LinePatrolEndpoints
{
    public static void MapLinePatrol(this WebApplication app)
    {
        var group = app.MapGroup("/api/line-patrol");
        PersonalAccess.RequireCurrent(group.MapPost("/access/verify", (HttpContext http) =>
            Results.Ok(LinePatrolReadService.Receipt(Authority(http)))), business: true);
        PersonalAccess.RequireCurrent(group.MapGet("/lines", async (LinePatrolReadService service, CancellationToken ct) =>
            Results.Ok(new { Items = await service.LinesAsync(ct) })), business: true);
        PersonalAccess.RequireCurrent(group.MapGet("/tasks", async (Guid lineId, int? page, int? pageSize,
            LinePatrolReadService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(lineId, page ?? 1, pageSize ?? 20, Authority(http), ct))), business: true);
        PersonalAccess.RequireCurrent(group.MapGet("/tasks/{id:guid}", async (Guid id, Guid? expectedTaskLineId,
            LinePatrolReadService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.DetailAsync(id, expectedTaskLineId, Authority(http), ct))), business: true);
    }
    private static PersonalAuthority Authority(HttpContext http) => (PersonalAuthority)http.Items[typeof(PersonalAuthority)]!;
}
