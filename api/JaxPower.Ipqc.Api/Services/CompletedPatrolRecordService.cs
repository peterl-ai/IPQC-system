using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class CompletedRecordIntegrityException : Exception
{
    public CompletedRecordIntegrityException() : base("Completed workflow record is inconsistent: the final approved submission or item snapshot is missing.") { }
}

public sealed class CompletedPatrolRecordService(IpqcDbContext db)
{
    public static readonly TimeZoneInfo PlantZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public async Task<PagedCompletedRecords> ListAsync(string? taskNo, string? plan, string? standard,
        string? factory, string? line, string? inspector, string? shift, string? result,
        DateOnly? completedFrom, DateOnly? completedTo, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PatrolTasks.AsNoTracking().Where(x => x.Status == "Completed");
        if (!string.IsNullOrWhiteSpace(taskNo)) query = query.Where(x => x.TaskNo.Contains(taskNo.Trim()));
        if (!string.IsNullOrWhiteSpace(plan)) query = query.Where(x => x.PlanNameSnapshot!.Contains(plan.Trim()) || x.PlanNoSnapshot!.Contains(plan.Trim()));
        if (!string.IsNullOrWhiteSpace(standard)) query = query.Where(x => x.StandardNameSnapshot!.Contains(standard.Trim()));
        if (!string.IsNullOrWhiteSpace(factory)) query = query.Where(x => x.FactoryNameSnapshot!.Contains(factory.Trim()) || x.FactoryCodeSnapshot!.Contains(factory.Trim()));
        if (!string.IsNullOrWhiteSpace(line)) query = query.Where(x => x.LineNameSnapshot!.Contains(line.Trim()) || x.LineCodeSnapshot!.Contains(line.Trim()));
        if (!string.IsNullOrWhiteSpace(inspector)) query = query.Where(x => x.AssignedInspectorKey.Contains(inspector.Trim()));
        if (!string.IsNullOrWhiteSpace(shift)) query = query.Where(x => x.Submissions.Any(s => s.RevisionNo == x.CurrentRevisionNo && s.Shift == shift));
        if (!string.IsNullOrWhiteSpace(result)) query = query.Where(x => x.Submissions.Any(s => s.RevisionNo == x.CurrentRevisionNo && s.OverallInspectionResult == result));
        if (completedFrom is { } from)
        {
            var start = PlantMidnightUtc(from);
            query = query.Where(x => x.CompletedAtUtc >= start);
        }
        if (completedTo is { } to)
        {
            var end = PlantMidnightUtc(to.AddDays(1));
            query = query.Where(x => x.CompletedAtUtc < end);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CompletedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CompletedRecordSummary(x.Id, x.TaskNo, x.CompletedAtUtc,
                x.ScheduledOccurrenceUtc,
                x.Submissions.Where(s => s.RevisionNo == x.CurrentRevisionNo)
                    .Select(s => (DateTime?)s.SubmittedAtUtc).FirstOrDefault(),
                x.Submissions.Where(s => s.RevisionNo == x.CurrentRevisionNo)
                    .Select(s => s.OverallInspectionResult).FirstOrDefault(),
                x.CurrentRevisionNo, x.AssignedInspectorKey,
                x.Submissions.Where(s => s.RevisionNo == x.CurrentRevisionNo)
                    .Select(s => s.Shift).FirstOrDefault(), x.PlanNoSnapshot,
                x.PlanNameSnapshot, x.StandardNameSnapshot, x.FactoryCodeSnapshot,
                x.FactoryNameSnapshot, x.WorkshopCodeSnapshot, x.LineCodeSnapshot,
                x.LineNameSnapshot, x.MaterialCodeSnapshot,
                x.Reviews.Where(r => r.RevisionNo == x.CurrentRevisionNo && r.Decision == "Approved")
                    .Select(r => r.Reviewer).FirstOrDefault(),
                x.Reviews.Where(r => r.RevisionNo == x.CurrentRevisionNo && r.Decision == "Approved")
                    .Select(r => (DateTime?)r.ReviewedAtUtc).FirstOrDefault()))
            .ToListAsync(ct);
        return new(rows.Select(x => x with {
            CompletedAtUtc = Utc(x.CompletedAtUtc), ScheduledOccurrenceUtc = Utc(x.ScheduledOccurrenceUtc),
            SubmittedAtUtc = Utc(x.SubmittedAtUtc), ApprovedAtUtc = Utc(x.ApprovedAtUtc)
        }).ToList(), page, pageSize, total);
    }

    public async Task<CompletedPatrolReport?> GetReportAsync(Guid id, CancellationToken ct)
    {
        var task = await db.PatrolTasks.AsNoTracking()
            .Where(x => x.Id == id && x.Status == "Completed")
            .Include(x => x.Items)
            .Include(x => x.Submissions).ThenInclude(x => x.Items).ThenInclude(x => x.Samples)
            .Include(x => x.Reviews)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        if (task is null) return null;
        if (task.CurrentRevisionNo < 1) throw new CompletedRecordIntegrityException();
        var final = task.Submissions.SingleOrDefault(x => x.RevisionNo == task.CurrentRevisionNo);
        var approved = task.Reviews.SingleOrDefault(x => x.RevisionNo == task.CurrentRevisionNo && x.Decision == "Approved");
        if (final is null || approved is null || final.Items.Count != task.Items.Count ||
            final.Items.Select(x => x.PatrolTaskItemId).Distinct().Count() != task.Items.Count ||
            final.Items.Any(x => task.Items.All(i => i.Id != x.PatrolTaskItemId)))
            throw new CompletedRecordIntegrityException();

        var snapshots = task.Items.ToDictionary(x => x.Id);
        var items = final.Items.OrderBy(x => x.SequenceNo).Select(x =>
        {
            var definition = snapshots[x.PatrolTaskItemId];
            return new CompletedRecordItem(definition.SequenceNo, definition.ProcessCode,
                definition.ProcessName, definition.InspectionItemCategory, definition.InspectionItem,
                definition.InspectionContent, definition.InspectionType,
                definition.LowerLimitOperator, definition.LowerLimitValue,
                definition.UpperLimitOperator, definition.UpperLimitValue,
                definition.SamplingPlan, definition.SampleCount, definition.PhotoRequirement,
                definition.DefectLevel, x.IsNa, x.JudgmentResult, Utc(x.InspectedAtUtc),
                x.MachineCode, x.Series, x.Mold, x.AbnormalType, x.AbnormalCause, x.Remarks,
                x.Samples.OrderBy(s => s.SequenceNo).Select(Sample).ToList());
        }).ToList();
        var reviews = task.Reviews.ToDictionary(x => x.RevisionNo);
        var history = task.Submissions.OrderBy(x => x.RevisionNo).Select(x =>
        {
            reviews.TryGetValue(x.RevisionNo, out var review);
            return new CompletedRecordRevision(x.RevisionNo, x.Shift, x.OverallInspectionResult,
                x.SubmittedBy, Utc(x.SubmittedAtUtc), review?.Decision, review?.Reviewer,
                Utc(review?.ReviewedAtUtc), review?.Reason,
                x.Items.OrderBy(i => i.SequenceNo).Select(i => new CompletedRecordRevisionItem(
                    i.SequenceNo, i.IsNa, i.JudgmentResult, Utc(i.InspectedAtUtc),
                    i.Samples.OrderBy(s => s.SequenceNo).Select(Sample).ToList())).ToList());
        }).ToList();
        var summary = new CompletedRecordSummary(task.Id, task.TaskNo, Utc(task.CompletedAtUtc),
            Utc(task.ScheduledOccurrenceUtc), Utc(final.SubmittedAtUtc), final.OverallInspectionResult,
            final.RevisionNo, task.AssignedInspectorKey, final.Shift, task.PlanNoSnapshot,
            task.PlanNameSnapshot, task.StandardNameSnapshot, task.FactoryCodeSnapshot,
            task.FactoryNameSnapshot, task.WorkshopCodeSnapshot, task.LineCodeSnapshot,
            task.LineNameSnapshot, task.MaterialCodeSnapshot, approved.Reviewer, Utc(approved.ReviewedAtUtc));
        return new(summary, Utc(task.GeneratedAtUtc), Utc(task.StartedAtUtc), final.RevisionNo,
            final.SubmittedBy, Utc(final.SubmittedAtUtc), items, history);
    }

    private static CompletedRecordSample Sample(PatrolTaskSubmissionSample sample) =>
        new(sample.SequenceNo, sample.InspectionValue, sample.JudgmentResult, Utc(sample.InspectedAtUtc));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is { } time ? Utc(time) : null;

    public static DateTime PlantMidnightUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, PlantZone);
    }
}
