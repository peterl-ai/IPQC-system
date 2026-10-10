using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUsers(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization(IpqcPermissions.UsersRead);
        group.MapGet("", async (string? username, string? displayName, string? role, bool? active,
            int? page, int? pageSize, ApplicationUserService service, CancellationToken ct) =>
        {
            var number = page ?? 1; var size = pageSize ?? 10;
            if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["Invalid pagination."] });
            if (role is not null && !ApplicationUserService.ValidRole(role))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Invalid role."] });
            return Results.Ok(await service.ListAsync(username, displayName, role, active, number, size, ct));
        });
        group.MapGet("/{id:guid}", async (Guid id, ApplicationUserService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } row ? Results.Ok(row) : Results.NotFound());
        group.MapPost("", async (UserCreateInput input, ApplicationUserService service,
            HttpContext http, CancellationToken ct) =>
        {
            try {
                var row = await service.CreateAsync(input, IpqcPermissions.Username(http.User), ct);
                return Results.Created($"/api/users/{row.Id}", row);
            } catch (Exception ex) when (Expected(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.UsersWrite);
        group.MapPut("/{id:guid}", async (Guid id, UserUpdateInput input,
            ApplicationUserService service, HttpContext http, CancellationToken ct) =>
        {
            try { return await service.UpdateAsync(id, input, IpqcPermissions.UserId(http.User),
                IpqcPermissions.Username(http.User), ct) is { } row ? Results.Ok(row) : Results.NotFound(); }
            catch (Exception ex) when (Expected(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.UsersWrite);
        group.MapPost("/{id:guid}/activate", async (Guid id, ApplicationUserService service,
            HttpContext http, CancellationToken ct) => await SetActive(id, true, service, http, ct))
            .RequireAuthorization(IpqcPermissions.UsersWrite);
        group.MapPost("/{id:guid}/deactivate", async (Guid id, ApplicationUserService service,
            HttpContext http, CancellationToken ct) => await SetActive(id, false, service, http, ct))
            .RequireAuthorization(IpqcPermissions.UsersWrite);
        group.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordInput input,
            ApplicationUserService service, HttpContext http, CancellationToken ct) =>
        {
            try { return await service.ResetPasswordAsync(id, input.TemporaryPassword,
                IpqcPermissions.Username(http.User), ct) is { } row ? Results.Ok(row) : Results.NotFound(); }
            catch (Exception ex) when (Expected(ex)) { return Error(ex); }
        }).RequireAuthorization(IpqcPermissions.UsersWrite);
        group.MapPost("/{id:guid}/unlock", async (Guid id, ApplicationUserService service,
            HttpContext http, CancellationToken ct) =>
            await service.UnlockAsync(id, IpqcPermissions.Username(http.User), ct) is { } row
                ? Results.Ok(row) : Results.NotFound()).RequireAuthorization(IpqcPermissions.UsersWrite);

        app.MapGet("/api/user-lookups/ipqa", async (IpqcDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ApplicationUsers.AsNoTracking()
                .Where(x => x.IsActive && x.Role == "IPQA").OrderBy(x => x.Username)
                .Select(x => new { x.Id, x.Username, x.DisplayName }).ToListAsync(ct)))
            .RequireAuthorization(IpqcPermissions.PlansRead);
    }

    private static async Task<IResult> SetActive(Guid id, bool active, ApplicationUserService service,
        HttpContext http, CancellationToken ct)
    {
        try { return await service.SetActiveAsync(id, active, IpqcPermissions.UserId(http.User),
            IpqcPermissions.Username(http.User), ct) is { } row ? Results.Ok(row) : Results.NotFound(); }
        catch (Exception ex) when (Expected(ex)) { return Error(ex); }
    }
    private static bool Expected(Exception ex) => ex is UserInputException or UserConflictException;
    private static IResult Error(Exception ex) => ex switch {
        UserInputException input => Results.ValidationProblem(input.Errors),
        UserConflictException conflict => Results.Problem(statusCode: 409, title: conflict.Message),
        _ => throw ex
    };
}
