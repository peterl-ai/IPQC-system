using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class LinePatrolReadService(IpqcDbContext db, PersonalAccessOptions options)
{
    public static OfflineAccessReceipt Receipt(PersonalAuthority authority) => new(1, authority.UserId,
        authority.PersonalSessionId, authority.EnvironmentId, authority.VerifiedAtUtc,
        authority.VerifiedAtUtc.AddHours(72) < authority.PersonalSessionExpiresAtUtc
            ? authority.VerifiedAtUtc.AddHours(72) : authority.PersonalSessionExpiresAtUtc,
        authority.PersonalSessionExpiresAtUtc);

    public async Task<List<PersonalLine>> LinesAsync(CancellationToken ct)
    {
        var ids = options.ConfiguredLines();
        var lines = await db.ProductionLines.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).ToListAsync(ct);
        if (lines.Count != 2) throw new PersonalAccessException("LineConfigurationUnavailable", 503);
        return ids.Select((id, i) => { var row = lines.Single(x => x.Id == id); return new PersonalLine(id, row.Code, row.Name, i + 1); }).ToList();
    }

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
        var availability = Availability(task);
        if (availability != "FoundationOnly") throw new PersonalAccessException(
            availability == "UnsupportedSnapshot" ? "TaskSnapshotUnavailable" : "TaskReadContractUnavailable", 409);
        return new(Summary(task, authority), task.PatrolPlanId, task.PatrolStandardId,
            task.FactoryCodeSnapshot, task.FactoryNameSnapshot, task.WorkshopCodeSnapshot,
            task.LineCodeSnapshot, task.MaterialCodeSnapshot, Utc(task.SourceStandardUpdatedAtUtc!.Value),
            task.Items.OrderBy(x => x.SequenceNo).Select(x => new LineTaskDefinition(x.Id,
                x.SourcePatrolStandardItemId, x.SequenceNo, x.ProcessCode, x.ProcessName,
                x.InspectionItemCategory, x.InspectionItem, x.InspectionContent, x.UpperLimitOperator,
                x.UpperLimitValue, x.LowerLimitOperator, x.LowerLimitValue, x.InspectionType,
                x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel)).ToList());
    }

    private static string Availability(PatrolTask task)
    {
        if (task.Status is not ("PendingInspection" or "InProgress") || task.CurrentRevisionNo != 0 ||
            task.Submissions.Count != 0 || task.Reviews.Count != 0 || task.SubmittedAtUtc is not null ||
            task.CompletedAtUtc is not null || task.OverallInspectionResult is not null ||
            task.Items.Any(x => x.IsNa is not null || x.JudgmentResult is not null || x.InspectedAtUtc is not null ||
                x.MachineCode is not null || x.Series is not null || x.Mold is not null || x.AbnormalType is not null ||
                x.AbnormalCause is not null || x.Remarks is not null || x.Samples.Count != 0)) return "UnsupportedState";
        if (new[] { task.PlanNoSnapshot, task.PlanNameSnapshot, task.StandardNameSnapshot, task.LineNameSnapshot }
            .Any(string.IsNullOrWhiteSpace) || task.SourceStandardUpdatedAtUtc is null || task.Items.Count == 0 ||
            task.Items.Any(x => x.SourcePatrolStandardItemId == Guid.Empty || x.SequenceNo < 1) ||
            task.Items.Select(x => x.SequenceNo).Distinct().Count() != task.Items.Count ||
            task.Items.Select(x => x.SourcePatrolStandardItemId).Distinct().Count() != task.Items.Count) return "UnsupportedSnapshot";
        return "FoundationOnly";
    }
    private static LineTaskSummary Summary(PatrolTask task, PersonalAuthority authority) => new(task.Id,
        task.TaskNo, task.LineId!.Value, (int)task.WorkflowVersion, task.Status,
        Utc(task.ScheduledOccurrenceUtc), Utc(task.GeneratedAtUtc), task.CurrentRevisionNo,
        task.PlanNoSnapshot, task.PlanNameSnapshot, task.StandardNameSnapshot, task.LineNameSnapshot,
        Availability(task), false, new(authority.UserId, authority.PersonalSessionId, authority.EnvironmentId,
            authority.VerifiedAtUtc, task.Id, task.LineId.Value, (int)task.WorkflowVersion));
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
