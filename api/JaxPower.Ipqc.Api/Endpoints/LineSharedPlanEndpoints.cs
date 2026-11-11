using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class LineSharedPlanEndpoints
{
    public static void MapLineSharedPlans(this WebApplication app)
    {
        var g = app.MapGroup("/api/line-patrol-plans").RequireAuthorization(IpqcPermissions.PlansRead);
        PersonalAccess.RequireCurrent(g.MapGet("/lines", async (ConfiguredLineDirectory s, CancellationToken ct) => Results.Ok(await s.LinesAsync(ct))), false);
        PersonalAccess.RequireCurrent(g.MapGet("", async (Guid? lineId, string? planNo, string? name, bool? enabled, int? page, int? pageSize,
            LineSharedPlanService s, CancellationToken ct) => Results.Ok(await s.ListAsync(lineId, planNo, name, enabled, page ?? 1, pageSize ?? 10, ct))), false);
        PersonalAccess.RequireCurrent(g.MapGet("/{id:guid}", async (Guid id, LineSharedPlanService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct))), false);
        PersonalAccess.RequireCurrent(g.MapPost("", async (SharedPlanInput input, LineSharedPlanService s, HttpContext http, CancellationToken ct) =>
        {
            var row = await s.SaveAsync(null, input, http.User, ct);
            return Results.Created($"/api/line-patrol-plans/{row.Id}", row);
        }).RequireAuthorization(IpqcPermissions.PlansWrite), false);
        PersonalAccess.RequireCurrent(g.MapPut("/{id:guid}", async (Guid id, SharedPlanInput input, LineSharedPlanService s, HttpContext http, CancellationToken ct) =>
            Results.Ok(await s.SaveAsync(id, input, http.User, ct))).RequireAuthorization(IpqcPermissions.PlansWrite), false);
        foreach (var enabled in new[] { true, false })
            PersonalAccess.RequireCurrent(g.MapPost("/{id:guid}/" + (enabled ? "enable" : "disable"), async (Guid id,
                SharedPlanRevisionInput input, LineSharedPlanService s, HttpContext http, CancellationToken ct) =>
                Results.Ok(await s.SetEnabledAsync(id, enabled, input.ExpectedRevision, http.User, ct))).RequireAuthorization(IpqcPermissions.PlansWrite), false);
    }
}
