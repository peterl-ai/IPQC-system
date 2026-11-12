using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class LinePatrolReadService(IpqcDbContext db, ConfiguredLineDirectory directory)
{
    public static OfflineAccessReceipt Receipt(PersonalAuthority authority) => new(1, authority.UserId,
        authority.PersonalSessionId, authority.EnvironmentId, authority.VerifiedAtUtc,
        authority.VerifiedAtUtc.AddHours(72) < authority.PersonalSessionExpiresAtUtc
            ? authority.VerifiedAtUtc.AddHours(72) : authority.PersonalSessionExpiresAtUtc,
        authority.PersonalSessionExpiresAtUtc);

    public Task<List<PersonalLine>> LinesAsync(CancellationToken ct) => directory.LinesAsync(ct);

    private IQueryable<PatrolTask> Tasks() => db.PatrolTasks.AsNoTracking()
        .Where(x => x.WorkflowVersion == WorkflowVersion.LineSharedV1)
        .Include(x => x.Items).ThenInclude(x => x.Samples).Include(x => x.Submissions).Include(x => x.Reviews).AsSplitQuery();

    public async Task<PagedLineTasks> ListAsync(Guid lineId, int page, int pageSize, PersonalAuthority authority, CancellationToken ct)
    {
        var lines = await LinesAsync(ct);
        if (lineId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new PersonalAccessException("InvalidLineTaskQuery", 400);
        if (lines.All(x => x.Id != lineId)) throw new PersonalAccessException("NotFound", 404);
        var query = Tasks().Where(x => x.LineId == lineId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.ScheduledOccurrenceUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(x => Summary(x, authority)).ToList(), page, pageSize, total);
    }

    public async Task<LineTaskDetail> DetailAsync(Guid id, Guid? expectedLine, PersonalAuthority authority, CancellationToken ct)
    {
        var lines = await LinesAsync(ct);
        var task = await Tasks().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (task?.LineId is not { } line || lines.All(x => x.Id != line)) throw new PersonalAccessException("NotFound", 404);
        if (expectedLine is { } expected && expected != line) throw new PersonalAccessException("TaskLineMismatch", 409);
        var availability = LineSharedTaskEligibility.Availability(task);
        if (availability != "FoundationOnly") throw new PersonalAccessException(
            availability == "UnsupportedSnapshot" ? "TaskSnapshotUnavailable" : "TaskReadContractUnavailable", 409);
        return new(Summary(task, authority), task.PatrolPlanId, task.PatrolStandardId,
            task.FactoryCodeSnapshot, task.FactoryNameSnapshot, task.WorkshopCodeSnapshot,
            task.LineCodeSnapshot, task.MaterialCodeSnapshot, Utc(task.SourceStandardUpdatedAtUtc!.Value),
            task.Items.OrderBy(x => x.SequenceNo).Select(x => new LineTaskDefinition(x.Id,
                x.SourcePatrolStandardItemId, x.SequenceNo, x.ProcessCode, x.ProcessName,
                x.InspectionItemCategory, x.InspectionItem, x.InspectionContent, x.UpperLimitOperator,
                x.UpperLimitValue, x.LowerLimitOperator, x.LowerLimitValue, x.InspectionType,
                x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel)).ToList(), false, PlanningSnapshot.Read(task));
    }

    private static LineTaskSummary Summary(PatrolTask task, PersonalAuthority authority) => new(task.Id,
        task.TaskNo, task.LineId!.Value, (int)task.WorkflowVersion, task.Status,
        Utc(task.ScheduledOccurrenceUtc), Utc(task.GeneratedAtUtc), task.CurrentRevisionNo,
        task.PlanNoSnapshot, task.PlanNameSnapshot, task.StandardNameSnapshot, task.LineNameSnapshot,
        LineSharedTaskEligibility.Availability(task), false, new(authority.UserId, authority.PersonalSessionId, authority.EnvironmentId,
            authority.VerifiedAtUtc, task.Id, task.LineId.Value, (int)task.WorkflowVersion), task.PlanningSnapshotVersion, PlanningSnapshot.Read(task)?.SourcePlanRevision,
        PlanningSnapshot.Read(task)?.Round, PlanningSnapshot.Read(task)?.ScheduledLocalDate, PlanningSnapshot.Status(task));
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
