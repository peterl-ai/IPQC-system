namespace JaxPower.Ipqc.Api.Contracts;

public sealed record LoginInput(string? Username, string? Password, string? ClientType = null);
public sealed record RefreshInput(string? RefreshToken = null);
public sealed record ChangePasswordInput(string? CurrentPassword, string? NewPassword);
public sealed record CurrentUserOutput(Guid Id, string Username, string DisplayName, string Role,
    bool IsActive, bool MustChangePassword, IReadOnlyList<string> Permissions);
public sealed record AuthOutput(string AccessToken, DateTime AccessExpiresAtUtc,
    CurrentUserOutput User, string? RefreshToken = null);
