using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace JaxPower.Ipqc.Api.Services;

public sealed class LocalAuthService(IpqcDbContext db, IPasswordHasher<ApplicationUser> hasher,
    AuthenticationClock authClock, JwtSettings settings)
{
    private TimeProvider clock => authClock.Clock;
    public const string RefreshCookie = "ipqc_refresh";

    public static string Normalize(string username) => username.Trim().ToUpperInvariant();
    public static bool ValidPassword(string? password) => password is { Length: >= 10 and <= 256 } &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);
    public static CurrentUserOutput Current(ApplicationUser user) => new(user.Id, user.Username,
        user.DisplayName, user.Role, user.IsActive, user.MustChangePassword, IpqcPermissions.ForRole(user.Role));

    public async Task<(AuthOutput Output, string RefreshToken)?> LoginAsync(string? username, string? password,
        string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || password is null || username.Length > 100) return null;
        var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.NormalizedUsername == Normalize(username), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (user is null || !user.IsActive || user.LockoutEndUtc > now) return null;
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.FailedLoginCount = 0;
                user.LockoutEndUtc = now.AddMinutes(15);
            }
            await db.SaveChangesAsync(ct);
            return null;
        }
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);
        var issued = Issue(user, now, now.AddDays(settings.RefreshDays), userAgent);
        await db.SaveChangesAsync(ct);
        return (issued.Output, issued.RefreshToken);
    }

    public async Task<(AuthOutput Output, string RefreshToken)?> RefreshAsync(string? token,
        string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return null;
        var hash = TokenHash(token);
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.RefreshTokenSessions.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now ||
            !session.User.IsActive || session.AuthVersion != session.User.AuthVersion) return null;
        var replaced = await db.RefreshTokenSessions.Where(x => x.Id == session.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), ct);
        if (replaced != 1) return null;
        var issued = Issue(session.User, now, session.ExpiresAtUtc, userAgent);
        session.ReplacedBySessionId = issued.SessionId;
        await db.SaveChangesAsync(ct);
        return (issued.Output, issued.RefreshToken);
    }

    public async Task LogoutAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return;
        var hash = TokenHash(token);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokenSessions.Where(x => x.TokenHash == hash && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), ct);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string? currentPassword,
        string? newPassword, CancellationToken ct)
    {
        if (!ValidPassword(newPassword) || currentPassword is null) return false;
        var user = await db.ApplicationUsers.FindAsync([userId], ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return false;
        var now = clock.GetUtcNow().UtcDateTime;
        user.PasswordHash = hasher.HashPassword(user, newPassword!);
        user.MustChangePassword = false;
        user.AuthVersion++;
        user.UpdatedAtUtc = now;
        user.UpdatedBy = user.Username;
        await db.RefreshTokenSessions.Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private (AuthOutput Output, string RefreshToken, Guid SessionId) Issue(ApplicationUser user, DateTime now, DateTime refreshExpiry, string? userAgent)
    {
        var expiry = now.AddMinutes(settings.AccessMinutes);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("username", user.Username), new Claim("auth_version", user.AuthVersion.ToString()) };
        var key = new SymmetricSecurityKey(settings.Key);
        var jwt = new JwtSecurityToken(issuer: settings.Issuer, audience: settings.Audience,
            claims: claims, notBefore: now, expires: expiry,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var sessionId = Guid.NewGuid();
        db.RefreshTokenSessions.Add(new RefreshTokenSession { Id = sessionId, UserId = user.Id,
            TokenHash = TokenHash(refresh), AuthVersion = user.AuthVersion, CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiry, UserAgent = userAgent?[..Math.Min(userAgent.Length, 500)] });
        return (new AuthOutput(new JwtSecurityTokenHandler().WriteToken(jwt), expiry, Current(user)), refresh, sessionId);
    }
}

public sealed class JwtSettings
{
    public required byte[] Key { get; init; }
    public string Issuer { get; init; } = "JaxPower.Ipqc";
    public string Audience { get; init; } = "JaxPower.Ipqc.Client";
    public int AccessMinutes { get; init; } = 15;
    public int RefreshDays { get; init; } = 14;
}

public sealed class AuthenticationClock(TimeProvider clock)
{
    public TimeProvider Clock { get; } = clock;
}
