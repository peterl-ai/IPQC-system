using System.Data;
using System.Security.Claims;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

// Account (NO KEY UPDATE) -> common planning gate -> sorted line roots -> Plan.
// NO KEY UPDATE conflicts with security writers, but is compatible with provisioning audit FK KEY SHARE.
public sealed class LineSharedPlanningStore(IpqcDbContext db, PersonalAccess access)
{
    public async Task<T> WriteAsync<T>(ClaimsPrincipal? actor, Func<Task<T>> operation, CancellationToken ct)
    {
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
                        await ValidateActor(actor, ct);
                        var result = await operation();
                        await db.SaveChangesAsync(ct);
                        await tx!.CommitAsync(ct);
                        return result;
                    }
                    finally { connection.DefaultTimeout = timeout; await db.Database.CloseConnectionAsync(); }
                }
                if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Unsupported planning provider.");
                await using var pg = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
                if (actor is not null)
                {
                    if (!Guid.TryParse(actor.FindFirstValue("sub"), out var uid))
                        throw new PersonalAccessException("PersonalSessionRequired", 401);
                    _ = await db.ApplicationUsers.FromSqlInterpolated(
                        $"SELECT * FROM \"ApplicationUsers\" WHERE \"Id\" = {uid} FOR NO KEY UPDATE").AsNoTracking().ToListAsync(ct);
                }
                await ValidateActor(actor, ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(495051, 3203)", ct);
                var output = await operation();
                await db.SaveChangesAsync(ct);
                await pg.CommitAsync(ct);
                return output;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 } ||
                ex.InnerException is Npgsql.PostgresException { SqlState: "23505" or "23503" or "23514" })
            { db.ChangeTracker.Clear(); throw new PersonalAccessException("SharedPlanConflict", 409); }
            catch (Exception ex) when (PersonalSessionStore.IsTransient(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt >= 2) throw new PersonalAccessException("SharedPlanningTemporarilyUnavailable", 503);
                await Task.Delay(40 * (attempt + 1), ct);
            }
        }
    }

    private async Task ValidateActor(ClaimsPrincipal? actor, CancellationToken ct)
    {
        if (actor is null) return; // Scheduler / existing Standard mutations retain their existing authorization.
        var authority = await access.ValidateAsync(actor, false, ct);
        var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == authority.UserId, ct);
        if (user.Role is not ("Admin" or "PQE") || user.MustChangePassword)
            throw new PersonalAccessException("Forbidden", 403);
    }

    public async Task LockLinesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        foreach (var id in ids.Distinct().Order())
            _ = await db.ProductionLines.FromSqlInterpolated(
                $"SELECT * FROM \"ProductionLines\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().ToListAsync(ct);
    }
}
