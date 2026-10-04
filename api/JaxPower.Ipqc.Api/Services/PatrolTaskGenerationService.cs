using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PatrolTaskGenerationService(IpqcDbContext db, TimeProvider clock, IConfiguration configuration)
{
    public async Task<int> GenerateDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var window = Math.Clamp(configuration.GetValue("Scheduler:CatchUpWindowHours", 24), 1, 168);
        var earliest = now.AddHours(-window);
        var plans = await db.PatrolPlans.AsNoTracking().Include(x => x.Assignees)
            .Where(x => x.IsEnabled && x.EffectiveStartUtc <= now &&
                (x.EffectiveEndUtc == null || x.EffectiveEndUtc >= earliest))
            .ToListAsync(ct);
        var created = 0;
        foreach (var plan in plans)
        {
            var assignee = plan.Assignees.SingleOrDefault(x => x.IsActive);
            if (assignee is null) continue; // Invalid data cannot create an unassigned task.
            var lower = new[] { earliest, plan.EffectiveStartUtc, plan.GenerationNotBeforeUtc }.Max();
            var upper = plan.EffectiveEndUtc is { } end && end < now ? end : now;
            if (upper < lower) continue;
            var occurrences = PatrolSchedule.Occurrences(PatrolSchedule.Read(plan.ScheduleDefinition),
                plan.TimeZoneId, plan.EffectiveStartUtc, lower, upper);
            foreach (var occurrence in occurrences)
            {
                if (occurrence <= plan.GenerationNotBeforeUtc) continue;
                if (await db.PatrolTasks.AsNoTracking().AnyAsync(x => x.PatrolPlanId == plan.Id && x.ScheduledOccurrenceUtc == occurrence, ct)) continue;
                var id = Guid.NewGuid();
                var task = new PatrolTask { Id = id, TaskNo = $"PT-{id:N}".ToUpperInvariant(),
                    PatrolPlanId = plan.Id, PatrolStandardId = plan.PatrolStandardId,
                    AssignedInspectorKey = assignee.AssigneeKey, ScheduledOccurrenceUtc = occurrence,
                    GeneratedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now };
                db.PatrolTasks.Add(task);
                try
                {
                    await db.SaveChangesAsync(ct);
                    created++;
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // A competing worker won this occurrence; the database unique key is authoritative.
                    db.Entry(task).State = EntityState.Detached;
                }
            }
        }
        return created;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 or 1555 } ||
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed class PatrolSchedulerBackgroundService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<PatrolSchedulerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Scheduler:Enabled", true)) return;
        var seconds = Math.Clamp(configuration.GetValue("Scheduler:PollIntervalSeconds", 60), 5, 3600);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PatrolTaskGenerationService>().GenerateDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Patrol task generation pass failed; retrying next poll."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
