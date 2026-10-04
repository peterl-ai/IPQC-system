using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PatrolTaskGenerationService(IpqcDbContext db, TimeProvider clock, IConfiguration configuration,
    ILogger<PatrolTaskGenerationService> logger)
{
    public async Task<int> GenerateDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var window = Math.Clamp(configuration.GetValue("Scheduler:CatchUpWindowHours", 24), 1, 168);
        var earliest = now.AddHours(-window);
        var plans = await db.PatrolPlans.AsNoTracking().Include(x => x.Assignees)
            .Include(x => x.PatrolStandard).ThenInclude(x => x.InspectionItems)
            .AsSplitQuery()
            .Where(x => x.IsEnabled && x.EffectiveStartUtc <= now &&
                (x.EffectiveEndUtc == null || x.EffectiveEndUtc >= earliest))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        var created = 0;
        foreach (var plan in plans)
        {
            ct.ThrowIfCancellationRequested();
            var planCreated = 0;
            try
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
                        GeneratedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now,
                        PlanNoSnapshot = plan.PlanNo, PlanNameSnapshot = plan.PlanName,
                        StandardNameSnapshot = plan.PatrolStandard.PatrolStandardName,
                        FactoryCodeSnapshot = plan.FactoryCode.Length > 0 ? plan.FactoryCode : plan.PatrolStandard.FactoryCode,
                        FactoryNameSnapshot = plan.FactoryName.Length > 0 ? plan.FactoryName : plan.PatrolStandard.FactoryName,
                        WorkshopCodeSnapshot = plan.PatrolStandard.WorkshopCode,
                        LineCodeSnapshot = plan.LineCode.Length > 0 ? plan.LineCode : plan.PatrolStandard.LineCode,
                        LineNameSnapshot = plan.LineName.Length > 0 ? plan.LineName : plan.PatrolStandard.LineName,
                        MaterialCodeSnapshot = plan.PatrolStandard.MaterialCode,
                        SourceStandardUpdatedAtUtc = plan.PatrolStandard.UpdatedAtUtc,
                        Items = plan.PatrolStandard.InspectionItems.OrderBy(x => x.SequenceNo).Select(x => new PatrolTaskItem
                        {
                            Id = Guid.NewGuid(), SourcePatrolStandardItemId = x.Id, SequenceNo = x.SequenceNo,
                            ProcessCode = x.ProcessCode, ProcessName = x.ProcessName,
                            InspectionItemCategory = x.InspectionItemCategory, InspectionItem = x.InspectionItem,
                            InspectionContent = x.InspectionContent, UpperLimitOperator = x.UpperLimitOperator,
                            UpperLimitValue = x.UpperLimitValue, LowerLimitOperator = x.LowerLimitOperator,
                            LowerLimitValue = x.LowerLimitValue, InspectionType = x.InspectionType,
                            SamplingPlan = x.SamplingPlan, SampleCount = x.SampleCount,
                            PhotoRequirement = x.PhotoRequirement, DefectLevel = x.DefectLevel
                        }).ToList() };
                    db.PatrolTasks.Add(task);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        planCreated++;
                    }
                    catch (DbUpdateException ex) when (IsOccurrenceUniqueViolation(ex))
                    {
                        // A competing worker won this occurrence; the database unique key is authoritative.
                        db.ChangeTracker.Clear();
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                DetachPendingTasks(plan.Id);
                throw;
            }
            catch (Exception ex)
            {
                DetachPendingTasks(plan.Id);
                logger.LogError(ex, "Patrol task generation failed for Patrol Plan {PatrolPlanId}; continuing with other plans.", plan.Id);
            }
            finally
            {
                created += planCreated;
            }
        }
        return created;
    }

    private void DetachPendingTasks(Guid planId)
    {
        // The scheduler only tracks its newly added task graphs; clear all pending children too.
        db.ChangeTracker.Clear();
    }

    private static bool IsOccurrenceUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        } sqlite && sqlite.Message.Contains(
            "PatrolTasks.PatrolPlanId, PatrolTasks.ScheduledOccurrenceUtc", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_PatrolTasks_PatrolPlanId_ScheduledOccurrenceUtc"
        };
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
