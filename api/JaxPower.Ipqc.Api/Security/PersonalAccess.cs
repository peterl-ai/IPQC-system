using System.Security.Claims;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Security;

public sealed class PersonalAccessOptions
{
    public string? EnvironmentId { get; init; }
    public string[] LineIds { get; init; } = [];
    public string? ValidEnvironment => EnvironmentId is { Length: > 0 and <= 200 } value &&
        value == value.Trim() ? value : null;
    public Guid[] ConfiguredLines()
    {
        if (LineIds.Length != 2 || LineIds.Any(x => !Guid.TryParse(x, out var id) || id == Guid.Empty))
            throw new PersonalAccessException("LineConfigurationUnavailable", 503);
        var ids = LineIds.Select(Guid.Parse).ToArray();
        if (ids[0] == ids[1]) throw new PersonalAccessException("LineConfigurationUnavailable", 503);
        return ids;
    }
}

public sealed record PersonalAuthority(Guid UserId, Guid PersonalSessionId, string EnvironmentId,
    DateTime VerifiedAtUtc, DateTime PersonalSessionExpiresAtUtc);

public sealed class PersonalAccess(IpqcDbContext db, AuthenticationClock clock, PersonalAccessOptions options)
{
    public const string FamilyClaim = "personal_session_id";
    public const string EnvironmentClaim = "personal_environment_id";
    public const string CurrentPolicy = "PersonalSession.Current";
    public static Guid? SignedFamily(ClaimsPrincipal principal) =>
        principal.FindAll(FamilyClaim).ToArray() is [{ Value: var value }] &&
        Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    public async Task<PersonalAuthority> ValidateAsync(ClaimsPrincipal principal, bool business, CancellationToken ct)
    {
        if (business && (!principal.IsInRole("IPQA") || principal.HasClaim("must_change_password", "true")))
            throw new PersonalAccessException("PersonalAccessForbidden", 403);
        var environment = options.ValidEnvironment ?? throw new PersonalAccessException("PersonalAccessConfigurationUnavailable", 503);
        var family = SignedFamily(principal) ?? throw new PersonalAccessException("PersonalSessionRequired", 401);
        if (principal.FindAll(EnvironmentClaim).ToArray() is not [{ Value: var env }] || env != environment ||
            principal.FindAll("sub").ToArray() is not [{ Value: var userText }] || !Guid.TryParse(userText, out var userId) ||
            principal.FindAll("auth_version").ToArray() is not [{ Value: var versionText }] || !int.TryParse(versionText, out var version))
            throw new PersonalAccessException("PersonalSessionInvalid", 401);
        var now = clock.Clock.GetUtcNow().UtcDateTime;
        // One statement observes committed account + current family, including atomic rotation.
        var current = await db.RefreshTokenSessions.AsNoTracking().Where(x => x.UserId == userId &&
            x.PersonalSessionId == family && x.PersonalSessionEnvironmentId == environment &&
            x.RevokedAtUtc == null && x.ExpiresAtUtc > now && x.AuthVersion == version &&
            x.User.IsActive && x.User.AuthVersion == version)
            .Select(x => new { x.ExpiresAtUtc, x.User.Role, x.User.MustChangePassword }).SingleOrDefaultAsync(ct);
        if (current is null) throw new PersonalAccessException("PersonalSessionInvalid", 401);
        if (business && (current.Role != "IPQA" || current.MustChangePassword))
            throw new PersonalAccessException("PersonalAccessForbidden", 403);
        return new(userId, family, environment, now, DateTime.SpecifyKind(current.ExpiresAtUtc, DateTimeKind.Utc));
    }

    public static RouteHandlerBuilder RequireCurrent(RouteHandlerBuilder route, bool business) => route
        .RequireAuthorization(business ? IpqcPermissions.LinePatrolRead : CurrentPolicy)
        .AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            var access = context.HttpContext.RequestServices.GetRequiredService<PersonalAccess>();
            context.HttpContext.Items[typeof(PersonalAuthority)] = await access.ValidateAsync(
                context.HttpContext.User, business, context.HttpContext.RequestAborted);
            return await next(context);
        });
}
