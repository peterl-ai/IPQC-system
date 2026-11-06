using System.Data;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class ProvisioningException(string code, int status = 409) : Exception(code)
{
    public int Status { get; } = status;
}

// Deliberately serializes low-volume provisioning writes only, including empty-line races.
// P1-03 credential/trust mutations must use this same transaction protocol.
public sealed class ProvisioningStore(IpqcDbContext db, TimeProvider clock)
{
    public const string PostgresLockSql = "SELECT pg_advisory_xact_lock(49505102)";
    public async Task<T> WriteAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                await db.Database.OpenConnectionAsync(ct);
                try
                {
                    // Acquire SQLite's writer reservation BEFORE reading state. No SELECT FOR UPDATE.
                    await using var native = ((SqliteConnection)db.Database.GetDbConnection())
                        .BeginTransaction(IsolationLevel.Serializable, deferred: false);
                    await using var transaction = await db.Database.UseTransactionAsync(native, ct);
                    var result = await operation();
                    await db.SaveChangesAsync(ct);
                    await native.CommitAsync(ct);
                    return result;
                }
                finally { await db.Database.CloseConnectionAsync(); }
            }
            if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Unsupported provisioning provider.");
            await using var pg = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlRawAsync(PostgresLockSql, ct);
            var output = await operation();
            await db.SaveChangesAsync(ct);
            await pg.CommitAsync(ct);
            return output;
        }
        catch (DbUpdateConcurrencyException) { throw new ProvisioningException("Conflict"); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { throw new ProvisioningException("Conflict"); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.LockNotAvailable)
        { throw new ProvisioningException("Conflict"); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 } ||
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation })
        { throw new ProvisioningException("Conflict"); }
    }

    public static void Revision(long actual, long expected)
    { if (expected < 1 || actual != expected) throw new ProvisioningException("Conflict"); }
    public static string Text(string? text, int max, bool required = true)
    {
        var value = text?.Trim() ?? "";
        if ((required && value.Length == 0) || value.Length > max) throw new ProvisioningException("InvalidInput", 400);
        return value;
    }
    public void Audit(Guid? line, Guid? device, string action, Guid actor, string username,
        ProvisioningSnapshot? before, ProvisioningSnapshot after, string? reason = null, Guid? changeId = null)
    {
        db.ProvisioningAuditEvents.Add(new ProvisioningAuditEvent { Id = Guid.NewGuid(), ChangeId = changeId ?? Guid.NewGuid(),
            ProductionLineId = line, DeviceId = device, OldLineId = before?.LineId, NewLineId = after.LineId,
            Action = action, ActorUserId = actor, ActorUsername = username,
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime, Reason = reason,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after) });
    }
    public async Task<ProvisioningPage<ProvisioningAuditOutput>> AuditAsync(Guid id, bool device, int page, int size, CancellationToken ct)
    {
        var query = db.ProvisioningAuditEvents.AsNoTracking().Where(x => device ? x.DeviceId == id : x.ProductionLineId == id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(x => new ProvisioningAuditOutput(x.Id, x.ChangeId, x.Action, x.ActorUserId, x.ActorUsername,
            DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), x.Reason,
            x.BeforeJson is null ? null : JsonSerializer.Deserialize<ProvisioningSnapshot>(x.BeforeJson),
            JsonSerializer.Deserialize<ProvisioningSnapshot>(x.AfterJson)!)).ToList(), page, size, total);
    }
}
