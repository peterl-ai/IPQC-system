namespace JaxPower.Ipqc.Api.Domain;

public sealed class ApplicationUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public int AuthVersion { get; set; } = 1;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = "";
    public List<RefreshTokenSession> RefreshSessions { get; set; } = [];
}

public sealed class RefreshTokenSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public int AuthVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public string? UserAgent { get; set; }
}
