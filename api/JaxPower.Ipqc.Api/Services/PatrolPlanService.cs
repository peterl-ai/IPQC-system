using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PatrolPlanService(IpqcDbContext db, TimeProvider clock)
{
    // Stable development fixture until Phase 1G provides real users and identity.
    public static readonly IReadOnlyList<AssigneeOption> DevelopmentAssignees =
        [new("dev-ipqa-1", "Development IPQA 1"), new("dev-ipqa-2", "Development IPQA 2")];

    public static Dictionary<string, string[]> Validate(PlanInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.PlanName) || input.PlanName.Length > 200)
            errors["PlanName"] = ["Plan Name must contain 1–200 non-whitespace characters."];
        if (input.PlanNo?.Length > 50) errors["PlanNo"] = ["Plan No. must be 50 characters or fewer."];
        Check(input.FactoryCode, 100, "FactoryCode");
        Check(input.FactoryName, 200, "FactoryName");
        Check(input.LineCode, 100, "LineCode");
        Check(input.LineName, 200, "LineName");
        if (input.PatrolStandardId == Guid.Empty) errors["PatrolStandardId"] = ["Select a Patrol Standard."];
        if (input.TimeZoneId != PatrolSchedule.PlantTimeZone) errors["TimeZoneId"] = ["Use the supported plant timezone America/New_York."];
        if (string.IsNullOrWhiteSpace(input.EffectiveStartLocal)) errors["EffectiveStartLocal"] = ["Effective Start is required."];
        PatrolSchedule.Normalize(input.Schedule, errors);
        if (input.IsEnabled && string.IsNullOrWhiteSpace(input.AssigneeKey))
            errors["AssigneeKey"] = ["An enabled plan requires one IPQA assignee."];
        if (!string.IsNullOrWhiteSpace(input.AssigneeKey) && !DevelopmentAssignees.Any(x => x.Key == input.AssigneeKey))
            errors["AssigneeKey"] = ["Select an available IPQA assignee."];
        return errors;

        void Check(string? value, int max, string field)
        {
            if (value?.Length > max) errors[field] = [$"Must be {max} characters or fewer."];
        }
    }

    public static Dictionary<string, string[]> ValidateDates(PlanInput input, out DateTime start, out DateTime? end)
    {
        var errors = new Dictionary<string, string[]>();
        start = default;
        end = null;
        if (input.TimeZoneId != PatrolSchedule.PlantTimeZone) return errors;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        if (!PatrolSchedule.TryLocal(input.EffectiveStartLocal, zone, out start))
            errors["EffectiveStartLocal"] = ["Use a valid plant-local date and time (YYYY-MM-DDTHH:mm). Nonexistent DST times are invalid."];
        if (!string.IsNullOrWhiteSpace(input.EffectiveEndLocal))
        {
            if (!PatrolSchedule.TryLocal(input.EffectiveEndLocal, zone, out var parsed))
                errors["EffectiveEndLocal"] = ["Use a valid plant-local date and time (YYYY-MM-DDTHH:mm)."];
            else end = parsed;
        }
        if (start != default && end is { } endUtc && endUtc <= start)
            errors["EffectiveEndLocal"] = ["Effective End must be later than Effective Start."];
        return errors;
    }

    public async Task<PagedPlans> ListAsync(string? planNo, string? planName, Guid? standardId,
        string? factoryCode, string? lineCode, bool? enabled, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PatrolPlans.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(planNo)) query = query.Where(x => x.PlanNo.Contains(planNo.Trim()));
        if (!string.IsNullOrWhiteSpace(planName)) query = query.Where(x => x.PlanName.Contains(planName.Trim()));
        if (standardId is { } sid) query = query.Where(x => x.PatrolStandardId == sid);
        if (!string.IsNullOrWhiteSpace(factoryCode)) query = query.Where(x => x.FactoryCode.Contains(factoryCode.Trim()));
        if (!string.IsNullOrWhiteSpace(lineCode)) query = query.Where(x => x.LineCode.Contains(lineCode.Trim()));
        if (enabled is { } active) query = query.Where(x => x.IsEnabled == active);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.PlanNo).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { Plan = x, StandardName = x.PatrolStandard.PatrolStandardName,
                AssigneeKey = x.Assignees.Where(a => a.IsActive).Select(a => a.AssigneeKey).FirstOrDefault() })
            .ToListAsync(ct);
        return new PagedPlans(rows.Select(x => Summary(x.Plan, x.StandardName, x.AssigneeKey)).ToList(), page, pageSize, total);
    }

    public async Task<PlanOutput?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.PatrolPlans.AsNoTracking().Include(x => x.PatrolStandard).Include(x => x.Assignees)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : Output(row);
    }

    public async Task<PlanOutput> CreateAsync(PlanInput input, DateTime start, DateTime? end, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = new PatrolPlan { Id = Guid.NewGuid(), CreatedBy = actor, CreatedAtUtc = now,
            GenerationNotBeforeUtc = now.AddTicks(-1) };
        Apply(row, input, start, end, now, actor);
        db.PatrolPlans.Add(row);
        await SaveChangesAsync(ct);
        return (await GetAsync(row.Id, ct))!;
    }

    public async Task<PlanOutput?> UpdateAsync(Guid id, PlanInput input, DateTime start, DateTime? end, string actor, CancellationToken ct)
    {
        var row = await db.PatrolPlans.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = PatrolSchedule.Normalize(input.Schedule, new());
        var active = row.Assignees.SingleOrDefault(x => x.IsActive)?.AssigneeKey;
        if (row.ScheduleDefinition != schedule || row.TimeZoneId != input.TimeZoneId ||
            row.EffectiveStartUtc != start || row.EffectiveEndUtc != end ||
            row.PatrolStandardId != input.PatrolStandardId || active != input.AssigneeKey ||
            (!row.IsEnabled && input.IsEnabled))
            row.GenerationNotBeforeUtc = now;
        Apply(row, input, start, end, now, actor);
        await SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<string> DeleteAsync(Guid id, CancellationToken ct)
    {
        var row = await db.PatrolPlans.FindAsync([id], ct);
        if (row is null) return "missing";
        if (await db.PatrolTasks.AnyAsync(x => x.PatrolPlanId == id, ct)) return "history";
        db.PatrolPlans.Remove(row);
        await db.SaveChangesAsync(ct);
        return "deleted";
    }

    public async Task<PlanOutput?> SetEnabledAsync(Guid id, bool enabled, string actor, CancellationToken ct)
    {
        var row = await db.PatrolPlans.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return null;
        if (enabled && row.Assignees.Count(x => x.IsActive) != 1) throw new InvalidOperationException("An enabled plan requires exactly one active assignee.");
        if (row.IsEnabled != enabled)
        {
            row.IsEnabled = enabled;
            row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = actor;
            if (enabled) row.GenerationNotBeforeUtc = row.UpdatedAtUtc;
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    private void Apply(PatrolPlan row, PlanInput input, DateTime start, DateTime? end, DateTime now, string actor)
    {
        row.PlanNo = string.IsNullOrWhiteSpace(input.PlanNo) ? (row.PlanNo.Length > 0 ? row.PlanNo : $"PLN-{row.Id:N}".ToUpperInvariant()) : input.PlanNo.Trim();
        row.PlanName = input.PlanName!.Trim();
        row.PatrolStandardId = input.PatrolStandardId;
        row.FactoryCode = input.FactoryCode?.Trim() ?? "";
        row.FactoryName = input.FactoryName?.Trim() ?? "";
        row.LineCode = input.LineCode?.Trim() ?? "";
        row.LineName = input.LineName?.Trim() ?? "";
        row.IsEnabled = input.IsEnabled;
        row.EffectiveStartUtc = start;
        row.EffectiveEndUtc = end;
        row.ScheduleDefinition = PatrolSchedule.Normalize(input.Schedule, new());
        row.TimeZoneId = input.TimeZoneId!;
        row.UpdatedAtUtc = now;
        row.UpdatedBy = actor;
        foreach (var assignee in row.Assignees) assignee.IsActive = assignee.AssigneeKey == input.AssigneeKey;
        if (!string.IsNullOrWhiteSpace(input.AssigneeKey) && !row.Assignees.Any(x => x.AssigneeKey == input.AssigneeKey))
        {
            var added = new PatrolPlanAssignee { Id = Guid.NewGuid(), AssigneeKey = input.AssigneeKey, IsActive = true };
            row.Assignees.Add(added);
            db.PatrolPlanAssignees.Add(added);
        }
    }

    private async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsPlanNoUniqueViolation(ex))
        {
            throw new DuplicatePlanNoException(ex);
        }
    }

    private static bool IsPlanNoUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        } sqlite && sqlite.Message.Contains("PatrolPlans.PlanNo", StringComparison.OrdinalIgnoreCase) ||
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_PatrolPlans_PlanNo"
        };

    private static PlanSummary Summary(PatrolPlan row, string standardName, string? assignee)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId);
        return new(row.Id, row.PlanNo, row.PlanName, row.PatrolStandardId, standardName,
            row.FactoryCode, row.FactoryName, row.LineCode, row.LineName, row.IsEnabled,
            PatrolSchedule.LocalText(row.EffectiveStartUtc, zone), row.EffectiveEndUtc is { } end ? PatrolSchedule.LocalText(end, zone) : null,
            row.TimeZoneId, PatrolSchedule.Read(row.ScheduleDefinition), assignee, DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));
    }

    private static PlanOutput Output(PatrolPlan row)
    {
        var summary = Summary(row, row.PatrolStandard.PatrolStandardName, row.Assignees.SingleOrDefault(x => x.IsActive)?.AssigneeKey);
        return new(summary.Id, summary.PlanNo, summary.PlanName, summary.PatrolStandardId, summary.PatrolStandardName,
            summary.FactoryCode, summary.FactoryName, summary.LineCode, summary.LineName, summary.IsEnabled,
            summary.EffectiveStartLocal, summary.EffectiveEndLocal, summary.TimeZoneId, summary.Schedule,
            summary.AssigneeKey, DateTime.SpecifyKind(row.GenerationNotBeforeUtc, DateTimeKind.Utc), row.CreatedBy,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc), row.UpdatedBy, summary.UpdatedAtUtc);
    }
}

public sealed class DuplicatePlanNoException(Exception innerException)
    : Exception("Plan No. is already in use.", innerException);
