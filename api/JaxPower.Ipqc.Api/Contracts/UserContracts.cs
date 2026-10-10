namespace JaxPower.Ipqc.Api.Contracts;

public sealed record UserCreateInput(string? Username, string? DisplayName, string? Role,
    string? InitialPassword, bool IsActive);
public sealed record UserUpdateInput(string? DisplayName, string? Role);
public sealed record ResetPasswordInput(string? TemporaryPassword);
public sealed record UserOutput(Guid Id, string Username, string DisplayName, string Role, bool IsActive,
    bool MustChangePassword, DateTime? LockoutEndUtc, DateTime CreatedAtUtc, string CreatedBy,
    DateTime UpdatedAtUtc, string UpdatedBy);
public sealed record PagedUsers(List<UserOutput> Items, int Page, int PageSize, int Total);
