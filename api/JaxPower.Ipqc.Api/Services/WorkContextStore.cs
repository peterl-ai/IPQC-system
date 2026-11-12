using System.Data;
using System.Security.Claims;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class WorkContextStore(IpqcDbContext db, PersonalAccess access, PersonalAccessOptions options)
{
    public async Task<T> RunAsync<T>(ClaimsPrincipal actor, bool admin, Guid? taskId, IEnumerable<Guid> contextIds,
        Func<PersonalAuthority, Task<T>> operation, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true)
            throw new PersonalAccessException("PersonalSessionRequired", 401);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                db.ChangeTracker.Clear();
                if (db.Database.IsSqlite())
                {
                    await db.Database.OpenConnectionAsync(ct);
                    var connection = (SqliteConnection)db.Database.GetDbConnection();
                    var timeout = connection.DefaultTimeout;
                    try
                    {
                        connection.DefaultTimeout = timeout <= 0 ? 5 : Math.Min(timeout, 5);
                        await using var native = connection.BeginTransaction(IsolationLevel.Serializable, deferred: false);
                        await using var tx = await db.Database.UseTransactionAsync(native, ct);
                        var result = await operation(await Authority(actor, admin, ct));
                        await db.SaveChangesAsync(ct);
                        await tx!.CommitAsync(ct);
                        return result;
                    }
                    finally { connection.DefaultTimeout = timeout; await db.Database.CloseConnectionAsync(); }
                }
                if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Unsupported WorkContext provider.");
                await using var pg = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
                if (!Guid.TryParse(actor.FindFirstValue("sub"), out var requester))
                    throw new PersonalAccessException("PersonalSessionRequired", 401);
                var ids = contextIds.Distinct().ToArray();
                var owners = await db.WorkContexts.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => x.UserId).ToListAsync(ct);
                foreach (var owner in owners.Append(requester).Distinct().Order())
                    _ = await db.ApplicationUsers.FromSqlInterpolated($"SELECT * FROM \"ApplicationUsers\" WHERE \"Id\" = {owner} FOR NO KEY UPDATE")
                        .AsNoTracking().ToListAsync(ct);
                var authority = await Authority(actor, admin, ct);
                if (taskId is { } task)
                {
                    foreach (var line in options.ConfiguredLines().Order())
                        _ = await db.ProductionLines.FromSqlInterpolated($"SELECT * FROM \"ProductionLines\" WHERE \"Id\" = {line} FOR SHARE")
                            .AsNoTracking().ToListAsync(ct);
                    _ = await db.PatrolTasks.FromSqlInterpolated($"SELECT * FROM \"PatrolTasks\" WHERE \"Id\" = {task} FOR UPDATE")
                        .AsNoTracking().ToListAsync(ct);
                }
                foreach (var id in ids.Order())
                    _ = await db.WorkContexts.FromSqlInterpolated($"SELECT * FROM \"WorkContexts\" WHERE \"Id\" = {id} FOR UPDATE")
                        .AsNoTracking().ToListAsync(ct);
                var output = await operation(authority);
                await db.SaveChangesAsync(ct);
                await pg.CommitAsync(ct);
                return output;
            }
            catch (Exception ex) when (PersonalSessionStore.IsTransient(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt >= 2) throw new PersonalAccessException("WorkContextTemporarilyUnavailable", 503);
                await Task.Delay(40 * (attempt + 1), ct);
            }
        }
    }

    private async Task<PersonalAuthority> Authority(ClaimsPrincipal actor, bool admin, CancellationToken ct)
    {
        var authority = await access.ValidateAsync(actor, !admin, ct);
        var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == authority.UserId, ct);
        if (user.MustChangePassword || user.Role != (admin ? "Admin" : "IPQA"))
            throw new PersonalAccessException("PersonalAccessForbidden", 403);
        return authority;
    }
}
