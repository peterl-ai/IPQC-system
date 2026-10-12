using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", async (LoginInput input, LocalAuthService auth,
            HttpContext http, CancellationToken ct) =>
        {
            var issued = await auth.LoginAsync(input.Username, input.Password,
                http.Request.Headers.UserAgent.ToString(), ct);
            if (issued is null) return Results.Problem(statusCode: 401, title: "Invalid username or password.");
            if (input.ClientType == "Native")
                return Results.Ok(issued.Value.Output with { RefreshToken = issued.Value.RefreshToken });
            SetCookie(http, issued.Value.RefreshToken, issued.Value.RefreshExpiresAtUtc, app.Environment.IsDevelopment());
            return Results.Ok(issued.Value.Output);
        }).AllowAnonymous().RequireRateLimiting("login");
        group.MapPost("/refresh", async (RefreshInput? input, LocalAuthService auth,
            HttpContext http, CancellationToken ct) =>
        {
            var native = !string.IsNullOrEmpty(input?.RefreshToken);
            var token = native ? input!.RefreshToken : http.Request.Cookies[LocalAuthService.RefreshCookie];
            var issued = await auth.RefreshAsync(token, http.Request.Headers.UserAgent.ToString(), ct);
            if (issued is null) { ClearCookie(http); return Results.Problem(statusCode: 401, title: "Session expired."); }
            if (native) return Results.Ok(issued.Value.Output with { RefreshToken = issued.Value.RefreshToken });
            SetCookie(http, issued.Value.RefreshToken, issued.Value.RefreshExpiresAtUtc, app.Environment.IsDevelopment());
            return Results.Ok(issued.Value.Output);
        }).AllowAnonymous().RequireRateLimiting("refresh");
        group.MapPost("/logout", async (RefreshInput? input, LocalAuthService auth,
            HttpContext http, CancellationToken ct) =>
        {
            await auth.LogoutAsync(input?.RefreshToken ?? http.Request.Cookies[LocalAuthService.RefreshCookie], ct);
            ClearCookie(http);
            return Results.NoContent();
        }).RequireAuthorization();
        group.MapPost("/change-password", async (ChangePasswordInput input, LocalAuthService auth,
            HttpContext http, CancellationToken ct) =>
        {
            var changed = await auth.ChangePasswordAsync(IpqcPermissions.UserId(http.User),
                input.CurrentPassword, input.NewPassword, ct);
            if (!changed) return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["password"] = ["Current password or new password is invalid."] });
            ClearCookie(http);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/me", async (HttpContext http, IpqcDbContext db, CancellationToken ct) =>
        {
            var id = IpqcPermissions.UserId(http.User);
            var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            return Results.Ok(LocalAuthService.Current(user));
        }).RequireAuthorization();
    }

    private static void SetCookie(HttpContext http, string token, DateTime expiresAtUtc, bool development) =>
        http.Response.Cookies.Append(LocalAuthService.RefreshCookie, token, new CookieOptions {
            HttpOnly = true, Secure = !development, SameSite = SameSiteMode.Strict,
            Path = "/api/auth", Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)) });

    private static void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(LocalAuthService.RefreshCookie, new CookieOptions { Path = "/api/auth" });
}
