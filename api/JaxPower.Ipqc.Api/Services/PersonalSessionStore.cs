using System.Data;
using JaxPower.Ipqc.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PersonalAccessException(string code, int status) : Exception(code)
{
    public int Status { get; } = status;
}

// Every existing-account/session writer uses account -> token lock order.
public sealed class PersonalSessionStore(IpqcDbContext db)
{
    public static bool IsTransient(Exception ex) => ex is SqliteException { SqliteErrorCode: 5 or 6 } ||
        ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.LockNotAvailable } ||
        ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: { } inner } && IsTransient(inner);

    public async Task<T> WriteAsync<T>(Guid userId, Func<Task<T>> operation, CancellationToken ct,
        bool serializable = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Services own the unit of work; never reuse a pre-lock tracked account.
                db.ChangeTracker.Clear();
                if (db.Database.IsSqlite())
                {
                    await db.Database.OpenConnectionAsync(ct);
                    var connection = (SqliteConnection)db.Database.GetDbConnection();
                    var previousTimeout = connection.DefaultTimeout;
                    try
                    {
                        connection.DefaultTimeout = Math.Min(previousTimeout, 5);
                        await using var native = connection
                            .BeginTransaction(IsolationLevel.Serializable, deferred: false);
                        await using var tx = await db.Database.UseTransactionAsync(native, ct);
                        var result = await operation();
                        await db.SaveChangesAsync(ct);
                        await native.CommitAsync(ct);
                        return result;
                    }
                    finally { connection.DefaultTimeout = previousTimeout; await db.Database.CloseConnectionAsync(); }
                }
                if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Unsupported personal-session provider.");
                await using var pg = await db.Database.BeginTransactionAsync(
                    serializable ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, ct);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
                // Materialize the locking SELECT; all authority reads happen afterwards.
                _ = await db.ApplicationUsers.FromSqlInterpolated(
                    $"SELECT * FROM \"ApplicationUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
                    .AsNoTracking().ToListAsync(ct);
                var output = await operation();
                await db.SaveChangesAsync(ct);
                await pg.CommitAsync(ct);
                return output;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt >= 2) throw new PersonalAccessException("PersonalAccessTemporarilyUnavailable", 503);
                await Task.Delay(TimeSpan.FromMilliseconds(40 * (attempt + 1)), ct);
            }
        }
    }
}
