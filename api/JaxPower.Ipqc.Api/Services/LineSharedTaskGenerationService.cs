using System.Globalization;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class LineSharedTaskGenerationService(IpqcDbContext db, LineSharedPlanningStore store,
    ConfiguredLineDirectory directory, TimeProvider clock, IConfiguration configuration,
    ILogger<LineSharedTaskGenerationService> logger)
{
    public async Task<int> GenerateDueAsync(CancellationToken ct = default)
    {
        var ids = await db.PatrolPlans.AsNoTracking().Where(x => x.WorkflowVersion == WorkflowVersion.LineSharedV1 &&
            x.SharedPlanRevision > 0 && x.IsEnabled).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            try
            {
                // Discover only a bounded candidate list under the gate. Every insertion reloads all facts.
                var candidates = await store.WriteAsync(null, async () =>
                {
                    var p = await db.PatrolPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                    return p is null ? [] : Due(p, LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime));
                }, ct);
                foreach (var occurrence in candidates)
                    count += await store.WriteAsync(null, async () =>
                    {
                        var lineId = await db.PatrolPlans.AsNoTracking().Where(x => x.Id == id).Select(x => x.LineId).SingleOrDefaultAsync(ct);
                        await store.LockLinesAsync(new[] { lineId }.OfType<Guid>(), ct);
                        var p = await db.PatrolPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                        var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
                        if (p is null || !Due(p, now).Contains(occurrence)) return 0;
                        var lines = await directory.LinesAsync(ct);
                        if (lines.All(x => x.Id != p.LineId)) return 0;
                        var line = await db.ProductionLines.AsNoTracking().SingleAsync(x => x.Id == p.LineId, ct);
                        var s = await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems).SingleOrDefaultAsync(x => x.Id == p.PatrolStandardId, ct);
                        if (s is null || !LineSharedPlanService.Ready(s)) return 0;
                        var collisions = await db.PatrolTasks.AsNoTracking().Where(x => x.WorkflowVersion == WorkflowVersion.LineSharedV1 &&
                            x.LineId == p.LineId && x.ScheduledOccurrenceUtc == occurrence).ToListAsync(ct);
                        if (collisions.Count > 0)
                        {
                            if (collisions.Any(x => x.PlanningSnapshotVersion is null))
                                logger.LogWarning("HistoricalOccurrenceConflict line {LineId} occurrence {Occurrence}", p.LineId, occurrence);
                            return 0;
                        }
                        // Bounded calendar fence also catches an occurrence remapped by a future timezone-rule change.
                        var local = TimeZoneInfo.ConvertTimeFromUtc(occurrence, LineSharedPlanService.Zone);
                        var date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        var round = local.Hour == 7 ? "Day" : "Night";
                        var from = occurrence.AddDays(-2); var to = occurrence.AddDays(2);
                        var calendar = await db.PatrolTasks.AsNoTracking().Where(x => x.WorkflowVersion == WorkflowVersion.LineSharedV1 &&
                            x.LineId == p.LineId && x.PlanningSnapshotVersion != null && x.ScheduledOccurrenceUtc >= from && x.ScheduledOccurrenceUtc <= to)
                            .Include(x => x.Items).ToListAsync(ct);
                        if (calendar.Any(x => PlanningSnapshot.Read(x) is { } frozen && frozen.ScheduledLocalDate == date && frozen.Round == round))
                        {
                            logger.LogWarning("CalendarRuleDrift line {LineId} date {LocalDate} round {Round}", p.LineId, date, round);
                            return 0;
                        }
                        // A database write condition, in addition to locked fresh reads, claims the exact admitted identity.
                        if (await db.PatrolPlans.Where(x => x.Id == p.Id && x.WorkflowVersion == WorkflowVersion.LineSharedV1 &&
                            x.LineId == p.LineId && x.SharedPlanRevision == p.SharedPlanRevision && x.IsEnabled)
                            .ExecuteUpdateAsync(u => u.SetProperty(x => x.SharedPlanRevision, x => x.SharedPlanRevision), ct) != 1) return 0;
                        var task = Build(p, s, line, occurrence, now);
                        task.PlanningSnapshotJson = PlanningSnapshot.Serialize(PlanningSnapshot.Freeze(task, p, s, line));
                        InitialInspectionIdentity.Create(task, now);
                        db.PatrolTasks.Add(task);
                        return 1;
                    }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Shared planning failed for {PlanId}; continuing", id);
            }
        }
        return count;
    }
    private IReadOnlyList<DateTime> Due(PatrolPlan p, DateTime now)
    {
        if (p.WorkflowVersion != WorkflowVersion.LineSharedV1 || p.SharedPlanRevision is not > 0 || !p.IsEnabled ||
            p.LineId is null || p.TimeZoneId != PatrolSchedule.PlantTimeZone || LineSharedPlanService.Rounds(p.ScheduleDefinition).Count == 0) return [];
        var hours = Math.Clamp(configuration.GetValue("Scheduler:CatchUpWindowHours", 24), 1, 168);
        var lower = new[] { now.AddHours(-hours), DateTime.SpecifyKind(p.EffectiveStartUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(p.GenerationNotBeforeUtc, DateTimeKind.Utc), DateTime.SpecifyKind(p.CreatedAtUtc, DateTimeKind.Utc) }.Max();
        var upper = p.EffectiveEndUtc is { } e && e < now ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : now;
        return PatrolSchedule.Occurrences(PatrolSchedule.Read(p.ScheduleDefinition), p.TimeZoneId,
            DateTime.SpecifyKind(p.EffectiveStartUtc, DateTimeKind.Utc), lower, upper).Where(x => x > p.GenerationNotBeforeUtc).ToList();
    }
    private static PatrolTask Build(PatrolPlan p, PatrolStandard s, ProductionLine line, DateTime occurrence, DateTime now)
    {
        var id = Guid.NewGuid();
        return new PatrolTask { Id = id, TaskNo = $"PT-{id:N}".ToUpperInvariant(), WorkflowVersion = WorkflowVersion.LineSharedV1,
            LineId = line.Id, PatrolPlanId = p.Id, PatrolStandardId = s.Id, PlanningSnapshotVersion = 1,
            AssignedInspectorKey = "", AssignedInspectorUserId = null, ScheduledOccurrenceUtc = occurrence,
            GeneratedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now, GenerationSource = "LineSharedScheduler",
            PlanNoSnapshot = p.PlanNo, PlanNameSnapshot = p.PlanName, StandardNameSnapshot = s.PatrolStandardName,
            FactoryCodeSnapshot = s.FactoryCode, FactoryNameSnapshot = s.FactoryName, WorkshopCodeSnapshot = s.WorkshopCode,
            LineCodeSnapshot = line.Code, LineNameSnapshot = line.Name, MaterialCodeSnapshot = s.MaterialCode,
            SourceStandardUpdatedAtUtc = DateTime.SpecifyKind(s.UpdatedAtUtc, DateTimeKind.Utc),
            Items = s.InspectionItems.OrderBy(x => x.SequenceNo).Select(x => new PatrolTaskItem {
                Id = Guid.NewGuid(), SourcePatrolStandardItemId = x.Id, SequenceNo = x.SequenceNo,
                ProcessCode = x.ProcessCode, ProcessName = x.ProcessName, InspectionItemCategory = x.InspectionItemCategory,
                InspectionItem = x.InspectionItem, InspectionContent = x.InspectionContent,
                UpperLimitOperator = x.UpperLimitOperator, UpperLimitValue = x.UpperLimitValue,
                LowerLimitOperator = x.LowerLimitOperator, LowerLimitValue = x.LowerLimitValue,
                InspectionType = x.InspectionType, SamplingPlan = x.SamplingPlan, SampleCount = x.SampleCount,
                PhotoRequirement = x.PhotoRequirement, DefectLevel = x.DefectLevel }).ToList() };
    }
}
