using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Services;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class PatrolPlanEndpoints
{
    public static void MapPatrolPlans(this WebApplication app)
    {
        var group = app.MapGroup("/api/patrol-plans").RequireAuthorization(IpqcPermissions.PlansRead);
        group.MapGet("", async (string? planNo, string? planName, Guid? patrolStandardId,
            string? factoryCode, string? lineCode, bool? enabled, int? page, int? pageSize,
            PatrolPlanService service, CancellationToken ct) =>
        {
            var number = page ?? 1;
            var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Page must be >= 1 and pageSize must be 1–100."] });
            return Results.Ok(await service.ListAsync(planNo, planName, patrolStandardId, factoryCode, lineCode, enabled, number, size, ct));
        });
        group.MapGet("/assignees", async (IpqcDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ApplicationUsers.AsNoTracking().Where(x => x.IsActive && x.Role == "IPQA")
                .OrderBy(x => x.Username).Select(x => new AssigneeOption(x.Id, x.Username, x.DisplayName)).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid id, PatrolPlanService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } row ? Results.Ok(row) : Results.Problem(statusCode: 404, title: "Patrol Plan not found."));
        group.MapPost("", async (PlanInput input, PatrolPlanService service, IpqcDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var errors = PatrolPlanService.Validate(input);
            var dateErrors = PatrolPlanService.ValidateDates(input, out var start, out var end);
            foreach (var (key, values) in dateErrors) errors[key] = values;
            if (!await db.PatrolStandards.AnyAsync(x => x.Id == input.PatrolStandardId, ct)) errors["PatrolStandardId"] = ["Select an existing Patrol Standard."];
            if (input.AssigneeUserId is not null && !await service.ValidAssigneeAsync(input.AssigneeUserId, ct))
                errors["AssigneeUserId"] = ["Select an active IPQA user."];
            if (!string.IsNullOrWhiteSpace(input.PlanNo) && await db.PatrolPlans.AnyAsync(x => x.PlanNo == input.PlanNo.Trim(), ct))
                errors["PlanNo"] = ["Plan No. is already in use."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            try
            {
                var row = await service.CreateAsync(input, start, end, IpqcPermissions.Username(http.User), ct);
                return Results.Created($"/api/patrol-plans/{row.Id}", row);
            }
            catch (DuplicatePlanNoException)
            {
                return DuplicatePlanNo();
            }
            catch (PlanAssignmentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["AssigneeUserId"] = [ex.Message] }); }
        }).RequireAuthorization(IpqcPermissions.PlansWrite);
        group.MapPut("/{id:guid}", async (Guid id, PlanInput input, PatrolPlanService service, IpqcDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var errors = PatrolPlanService.Validate(input);
            var dateErrors = PatrolPlanService.ValidateDates(input, out var start, out var end);
            foreach (var (key, values) in dateErrors) errors[key] = values;
            if (!await db.PatrolStandards.AnyAsync(x => x.Id == input.PatrolStandardId, ct)) errors["PatrolStandardId"] = ["Select an existing Patrol Standard."];
            if (input.AssigneeUserId is not null && !await service.ValidAssigneeAsync(input.AssigneeUserId, ct))
                errors["AssigneeUserId"] = ["Select an active IPQA user."];
            if (!string.IsNullOrWhiteSpace(input.PlanNo) && await db.PatrolPlans.AnyAsync(x => x.PlanNo == input.PlanNo.Trim() && x.Id != id, ct))
                errors["PlanNo"] = ["Plan No. is already in use."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            try
            {
                return await service.UpdateAsync(id, input, start, end, IpqcPermissions.Username(http.User), ct) is { } row
                    ? Results.Ok(row) : Results.Problem(statusCode: 404, title: "Patrol Plan not found.");
            }
            catch (DuplicatePlanNoException)
            {
                return DuplicatePlanNo();
            }
            catch (PlanAssignmentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["AssigneeUserId"] = [ex.Message] }); }
        }).RequireAuthorization(IpqcPermissions.PlansWrite);
        group.MapPost("/{id:guid}/enable", async (Guid id, PatrolPlanService service, HttpContext http, CancellationToken ct) =>
        {
            var current = await service.GetAsync(id, ct);
            if (current is null) return Results.Problem(statusCode: 404, title: "Patrol Plan not found.");
            if (!await service.ValidAssigneeAsync(current.AssigneeUserId, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["AssigneeUserId"] = ["Assign one active IPQA before enabling the plan."] });
            var row = await service.SetEnabledAsync(id, true, IpqcPermissions.Username(http.User), ct);
            return Results.Ok(row);
        }).RequireAuthorization(IpqcPermissions.PlansWrite);
        group.MapPost("/{id:guid}/disable", async (Guid id, PatrolPlanService service, HttpContext http, CancellationToken ct) =>
            await service.SetEnabledAsync(id, false, IpqcPermissions.Username(http.User), ct) is { } row
                ? Results.Ok(row) : Results.Problem(statusCode: 404, title: "Patrol Plan not found."))
            .RequireAuthorization(IpqcPermissions.PlansWrite);
        group.MapDelete("/{id:guid}", async (Guid id, PatrolPlanService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) switch
            {
                "deleted" => Results.NoContent(),
                "history" => Results.Problem(statusCode: 409, title: "Plan has generated tasks. Disable it instead."),
                _ => Results.Problem(statusCode: 404, title: "Patrol Plan not found.")
            }).RequireAuthorization(IpqcPermissions.PlansWrite);
    }

    private static IResult DuplicatePlanNo() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["PlanNo"] = ["Plan No. is already in use."] });
}
