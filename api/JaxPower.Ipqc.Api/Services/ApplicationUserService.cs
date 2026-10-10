using System.Data;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class UserConflictException(string message) : Exception(message);
public sealed class UserInputException(Dictionary<string, string[]> errors) : Exception("User validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ApplicationUserService(IpqcDbContext db, IPasswordHasher<ApplicationUser> hasher,
    TimeProvider clock)
{
    public static bool ValidRole(string? role) => role is "Admin" or "PQE" or "IPQA";
    public static UserOutput Output(ApplicationUser u) => new(u.Id, u.Username, u.DisplayName, u.Role,
        u.IsActive, u.MustChangePassword, u.LockoutEndUtc, u.CreatedAtUtc, u.CreatedBy,
        u.UpdatedAtUtc, u.UpdatedBy);

    public async Task<PagedUsers> ListAsync(string? username, string? displayName, string? role,
        bool? active, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ApplicationUsers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(username)) query = query.Where(x => x.Username.Contains(username.Trim()));
        if (!string.IsNullOrWhiteSpace(displayName)) query = query.Where(x => x.DisplayName.Contains(displayName.Trim()));
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(x => x.Role == role);
        if (active is { } yes) query = query.Where(x => x.IsActive == yes);
        var total = await query.CountAsync(ct);
        var users = await query.OrderBy(x => x.Username).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => new UserOutput(x.Id, x.Username,
                x.DisplayName, x.Role, x.IsActive, x.MustChangePassword, x.LockoutEndUtc,
                x.CreatedAtUtc, x.CreatedBy, x.UpdatedAtUtc, x.UpdatedBy)).ToListAsync(ct);
        return new(users, page, pageSize, total);
    }

    public async Task<UserOutput?> GetAsync(Guid id, CancellationToken ct) =>
        await db.ApplicationUsers.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new UserOutput(x.Id, x.Username, x.DisplayName, x.Role, x.IsActive,
                x.MustChangePassword, x.LockoutEndUtc, x.CreatedAtUtc, x.CreatedBy,
                x.UpdatedAtUtc, x.UpdatedBy)).SingleOrDefaultAsync(ct);

    public async Task<UserOutput> CreateAsync(UserCreateInput input, string actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Username) || input.Username.Trim().Length > 100)
            errors["Username"] = ["Username must contain 1–100 non-whitespace characters."];
        if (string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 200)
            errors["DisplayName"] = ["Display Name must contain 1–200 non-whitespace characters."];
        if (!ValidRole(input.Role)) errors["Role"] = ["Choose Admin, PQE, or IPQA."];
        if (!LocalAuthService.ValidPassword(input.InitialPassword))
            errors["InitialPassword"] = ["Use 10–256 characters with upper, lower, and numeric characters."];
        if (errors.Count > 0) throw new UserInputException(errors);
        var normalized = LocalAuthService.Normalize(input.Username!);
        if (await db.ApplicationUsers.AnyAsync(x => x.NormalizedUsername == normalized, ct))
            throw new UserConflictException("Username is already in use.");
        var now = clock.GetUtcNow().UtcDateTime;
        var user = new ApplicationUser { Id = Guid.NewGuid(), Username = input.Username!.Trim(),
            NormalizedUsername = normalized, DisplayName = input.DisplayName!.Trim(), Role = input.Role!,
            IsActive = input.IsActive, MustChangePassword = true, CreatedBy = actor,
            UpdatedBy = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
        user.PasswordHash = hasher.HashPassword(user, input.InitialPassword!);
        db.ApplicationUsers.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUsernameUnique(ex))
        { throw new UserConflictException("Username is already in use."); }
        return Output(user);
    }

    public async Task<UserOutput?> UpdateAsync(Guid id, UserUpdateInput input, Guid actorId,
        string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 200 || !ValidRole(input.Role))
            throw new UserInputException(new() { ["user"] = ["Enter a valid Display Name and Role."] });
        return await ChangeAsync(id, actorId, actor, input, null, ct);
    }

    public Task<UserOutput?> SetActiveAsync(Guid id, bool active, Guid actorId, string actor,
        CancellationToken ct) => ChangeAsync(id, actorId, actor, null, active, ct);

    private async Task<UserOutput?> ChangeAsync(Guid id, Guid actorId, string actor, UserUpdateInput? update,
        bool? active, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (user is null) return null;
            var nextRole = update?.Role ?? user.Role;
            var nextActive = active ?? user.IsActive;
            if (id == actorId && (!nextActive || nextRole != "Admin"))
                throw new UserConflictException("You cannot deactivate yourself or remove your Admin role.");
            if (user.IsActive && user.Role == "Admin" && (!nextActive || nextRole != "Admin") &&
                await db.ApplicationUsers.CountAsync(x => x.IsActive && x.Role == "Admin", ct) <= 1)
                throw new UserConflictException("The last active Admin must remain active.");
            if (user.Role == "IPQA" && (!nextActive || nextRole != "IPQA") &&
                (await db.PatrolPlanAssignees.AnyAsync(x => x.UserId == id && x.IsActive && x.PatrolPlan.IsEnabled, ct) ||
                 await db.PatrolTasks.AnyAsync(x => x.AssignedInspectorUserId == id && x.Status != "Completed", ct)))
                throw new UserConflictException("IPQA has an enabled Plan or unfinished Task assignment.");
            var changedSecurity = user.Role != nextRole || user.IsActive != nextActive;
            user.Role = nextRole!;
            user.IsActive = nextActive;
            if (update is not null) user.DisplayName = update.DisplayName!.Trim();
            user.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            user.UpdatedBy = actor;
            if (changedSecurity)
            {
                user.AuthVersion++;
                await RevokeSessionsAsync(id, user.UpdatedAtUtc, ct);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Output(user);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
            ex is SqliteException { SqliteErrorCode: 5 or 6 })
        { throw new UserConflictException("User state changed concurrently. Retry the operation."); }
    }

    public async Task<UserOutput?> ResetPasswordAsync(Guid id, string? password, string actor,
        CancellationToken ct)
    {
        if (!LocalAuthService.ValidPassword(password))
            throw new UserInputException(new() { ["TemporaryPassword"] = ["Use 10–256 characters with upper, lower, and numeric characters."] });
        var user = await db.ApplicationUsers.FindAsync([id], ct);
        if (user is null) return null;
        user.PasswordHash = hasher.HashPassword(user, password!);
        user.MustChangePassword = true;
        user.AuthVersion++;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        user.UpdatedBy = actor;
        await RevokeSessionsAsync(id, user.UpdatedAtUtc, ct);
        await db.SaveChangesAsync(ct);
        return Output(user);
    }

    public async Task<UserOutput?> UnlockAsync(Guid id, string actor, CancellationToken ct)
    {
        var user = await db.ApplicationUsers.FindAsync([id], ct);
        if (user is null) return null;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        user.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
        return Output(user);
    }

    private Task<int> RevokeSessionsAsync(Guid id, DateTime now, CancellationToken ct) =>
        db.RefreshTokenSessions.Where(x => x.UserId == id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), ct);

    private static bool IsUsernameUnique(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqlite &&
            sqlite.Message.Contains("ApplicationUsers.NormalizedUsername", StringComparison.OrdinalIgnoreCase) ||
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_ApplicationUsers_NormalizedUsername" };
}
