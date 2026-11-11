using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class LineSharedPlanService(IpqcDbContext db, LineSharedPlanningStore store,
    ConfiguredLineDirectory directory, TimeProvider clock, ILogger<LineSharedPlanService> logger)
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(PatrolSchedule.PlantTimeZone);
    public static DateTime Micro(DateTime utc) => DateTime.SpecifyKind(new DateTime(utc.Ticks - utc.Ticks % 10), DateTimeKind.Utc);
    public static List<string> Rounds(string json)
    {
        try
        {
            var s = PatrolSchedule.Read(json);
            if (s.Type != "daily" || s.IntervalHours is not null || s.DaysOfWeek is { Count: > 0 } ||
                s.Times is not { Count: > 0 and <= 2 } || s.Times.Distinct().Count() != s.Times.Count ||
                s.Times.Any(x => x is not ("07:00" or "19:00"))) return [];
            return s.Times.Order().Select(x => x == "07:00" ? "Day" : "Night").ToList();
        }
        catch (Exception ex) when (ex is JsonException or NullReferenceException or ArgumentException) { return []; }
    }
    public static bool Ready(PatrolStandard s) => !string.IsNullOrWhiteSpace(s.PatrolStandardName) && !string.IsNullOrWhiteSpace(s.LineName) &&
        s.InspectionItems.Count is > 0 and <= 1000 && s.InspectionItems.All(x => x.Id != Guid.Empty && x.SequenceNo > 0) &&
        s.InspectionItems.Select(x => x.SequenceNo).Distinct().Count() == s.InspectionItems.Count &&
        s.InspectionItems.All(x => InspectionJudgmentService.ValidateDefinition(x.InspectionType, x.SampleCount,
            x.LowerLimitOperator, x.LowerLimitValue, x.UpperLimitOperator, x.UpperLimitValue) is null);
    public static SharedPlanOutput Output(PatrolPlan p) => new(p.Id, p.PlanNo, p.PlanName, p.PatrolStandardId,
        p.LineId, Rounds(p.ScheduleDefinition), PatrolSchedule.LocalText(p.EffectiveStartUtc, Zone),
        p.EffectiveEndUtc is { } end ? PatrolSchedule.LocalText(end, Zone) : null, p.IsEnabled, p.SharedPlanRevision,
        p.SharedPlanRevision is > 0 ? "Admitted" : "HistoricalUnadmitted", Micro(p.GenerationNotBeforeUtc),
        Micro(p.CreatedAtUtc), Micro(p.UpdatedAtUtc), p.UpdatedBy);
    public async Task<SharedPlanPage> ListAsync(Guid? lineId, string? planNo, string? name, bool? enabled,
        int page, int size, CancellationToken ct)
    {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
            throw new PersonalAccessException("InvalidSharedPlanInput", 400);
        var q = db.PatrolPlans.AsNoTracking().Where(x => x.WorkflowVersion == WorkflowVersion.LineSharedV1);
        if (lineId is { } line) q = q.Where(x => x.LineId == line);
        if (!string.IsNullOrWhiteSpace(planNo)) q = q.Where(x => x.PlanNo.Contains(planNo.Trim()));
        if (!string.IsNullOrWhiteSpace(name)) q = q.Where(x => x.PlanName.Contains(name.Trim()));
        if (enabled is { } e) q = q.Where(x => x.IsEnabled == e);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.PlanNo).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(Output).ToList(), page, size, total);
    }
    public async Task<SharedPlanOutput> GetAsync(Guid id, CancellationToken ct) => Output(await Target(id, false, ct));
    private async Task<PatrolPlan> Target(Guid id, bool admitted, CancellationToken ct)
    {
        var p = await db.PatrolPlans.SingleOrDefaultAsync(x => x.Id == id && x.WorkflowVersion == WorkflowVersion.LineSharedV1, ct)
            ?? throw new PersonalAccessException("NotFound", 404);
        if (admitted && p.SharedPlanRevision is not > 0) throw new PersonalAccessException("SharedPlanUnadmitted", 409);
        return p;
    }
    private static void Revision(PatrolPlan p, long? expected)
    { if (expected is not > 0 || expected != p.SharedPlanRevision) throw new PersonalAccessException("SharedPlanConflict", 409); }
    private async Task<(DateTime Start, DateTime? End, string Schedule)> Validate(SharedPlanInput input, Guid? self, CancellationToken ct)
    {
        var lines = await directory.LinesAsync(ct);
        if (!lines.Any(x => x.Id == input.LineId) || input.PlanName is null || input.PlanName.Trim().Length is < 1 or > 200 ||
            input.PlanNo?.Trim().Length > 50 || input.Rounds is not { Count: > 0 and <= 2 } ||
            input.Rounds.Distinct().Count() != input.Rounds.Count || input.Rounds.Any(x => x is not ("Day" or "Night")) ||
            !PatrolSchedule.TryLocal(input.EffectiveStartLocal, Zone, out var start))
            throw new PersonalAccessException("InvalidSharedPlanInput", 400);
        DateTime? end = null;
        if (!string.IsNullOrEmpty(input.EffectiveEndLocal))
        {
            if (!PatrolSchedule.TryLocal(input.EffectiveEndLocal, Zone, out var e) || e <= start)
                throw new PersonalAccessException("InvalidSharedPlanInput", 400);
            end = e;
        }
        var standard = await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems)
            .SingleOrDefaultAsync(x => x.Id == input.PatrolStandardId, ct);
        if (standard is null || input.IsEnabled && !Ready(standard)) throw new PersonalAccessException("SharedStandardNotReady", 400);
        if (!string.IsNullOrWhiteSpace(input.PlanNo) && await db.PatrolPlans.AnyAsync(x => x.Id != self && x.PlanNo == input.PlanNo.Trim(), ct))
            throw new PersonalAccessException("DuplicateSharedPlanNo", 409);
        if (input.IsEnabled)
        {
            var others = await db.PatrolPlans.AsNoTracking().Where(x => x.Id != self && x.WorkflowVersion == WorkflowVersion.LineSharedV1 &&
                x.SharedPlanRevision > 0 && x.IsEnabled && x.LineId == input.LineId &&
                (x.EffectiveEndUtc == null || x.EffectiveEndUtc >= start) && (end == null || x.EffectiveStartUtc <= end)).ToListAsync(ct);
            if (others.Any(x => Rounds(x.ScheduleDefinition).Count == 0 || Rounds(x.ScheduleDefinition).Intersect(input.Rounds).Any()))
                throw new PersonalAccessException("SharedPlanOverlap", 409);
        }
        var schedule = PatrolSchedule.Normalize(new("daily", null, input.Rounds.Select(x => x == "Day" ? "07:00" : "19:00").ToList(), null), new());
        return (start, end, schedule);
    }
    public Task<SharedPlanOutput> SaveAsync(Guid? id, SharedPlanInput input, ClaimsPrincipal actor, CancellationToken ct) => store.WriteAsync(actor, async () =>
    {
        var oldLine = id is { } key ? await db.PatrolPlans.AsNoTracking().Where(x => x.Id == key).Select(x => x.LineId).SingleOrDefaultAsync(ct) : null;
        await store.LockLinesAsync(new[] { oldLine, input.LineId }.OfType<Guid>(), ct);
        var p = id is { } existing ? await Target(existing, true, ct) : null;
        if (p is not null) Revision(p, input.ExpectedRevision);
        var facts = await Validate(input, id, ct);
        var now = Micro(clock.GetUtcNow().UtcDateTime);
        var changedSource = p is not null && (p.PatrolStandardId != input.PatrolStandardId || p.LineId != input.LineId ||
            p.ScheduleDefinition != facts.Schedule || p.EffectiveStartUtc != facts.Start || p.EffectiveEndUtc != facts.End || !p.IsEnabled && input.IsEnabled);
        if (p is null)
        {
            p = new PatrolPlan { Id = Guid.NewGuid(), WorkflowVersion = WorkflowVersion.LineSharedV1, SharedPlanRevision = 1,
                CreatedAtUtc = now, CreatedBy = IpqcPermissions.Username(actor), GenerationNotBeforeUtc = now.AddTicks(-10) };
            db.PatrolPlans.Add(p);
        }
        else p.SharedPlanRevision = checked(p.SharedPlanRevision!.Value + 1);
        if (changedSource && now > p.GenerationNotBeforeUtc) p.GenerationNotBeforeUtc = now;
        p.PlanNo = string.IsNullOrWhiteSpace(input.PlanNo) ? (p.PlanNo.Length > 0 ? p.PlanNo : $"PLN-{p.Id:N}".ToUpperInvariant()) : input.PlanNo.Trim();
        p.PlanName = input.PlanName.Trim(); p.PatrolStandardId = input.PatrolStandardId; p.LineId = input.LineId;
        p.ScheduleDefinition = facts.Schedule; p.TimeZoneId = PatrolSchedule.PlantTimeZone;
        p.EffectiveStartUtc = facts.Start; p.EffectiveEndUtc = facts.End; p.IsEnabled = input.IsEnabled;
        p.UpdatedAtUtc = now; p.UpdatedBy = IpqcPermissions.Username(actor);
        logger.LogInformation("Shared Plan {PlanId} revision {Revision} write requested by {ActorId}", p.Id, p.SharedPlanRevision, actor.FindFirstValue("sub"));
        return Output(p);
    }, ct);
    public Task<SharedPlanOutput> SetEnabledAsync(Guid id, bool enabled, long revision, ClaimsPrincipal actor, CancellationToken ct) => store.WriteAsync(actor, async () =>
    {
        var line = await db.PatrolPlans.AsNoTracking().Where(x => x.Id == id).Select(x => x.LineId).SingleOrDefaultAsync(ct);
        await store.LockLinesAsync(new[] { line }.OfType<Guid>(), ct);
        var p = await Target(id, true, ct); Revision(p, revision);
        if (enabled) await Validate(new(p.PlanNo, p.PlanName, p.PatrolStandardId, p.LineId!.Value, Rounds(p.ScheduleDefinition),
            PatrolSchedule.LocalText(p.EffectiveStartUtc, Zone), p.EffectiveEndUtc is { } e ? PatrolSchedule.LocalText(e, Zone) : null, true), id, ct);
        if (p.IsEnabled != enabled)
        {
            p.IsEnabled = enabled; p.SharedPlanRevision = checked(p.SharedPlanRevision!.Value + 1);
            var now = Micro(clock.GetUtcNow().UtcDateTime);
            if (enabled && now > p.GenerationNotBeforeUtc) p.GenerationNotBeforeUtc = now;
            p.UpdatedAtUtc = now; p.UpdatedBy = IpqcPermissions.Username(actor);
        }
        return Output(p);
    }, ct);
}
