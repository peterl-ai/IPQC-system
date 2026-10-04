using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class TaskWorkflowException(string message) : Exception(message);
public sealed class TaskValidationException(Dictionary<string, string[]> errors) : Exception("Task validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public sealed class PatrolTaskExecutionService(IpqcDbContext db, TimeProvider clock, InspectionJudgmentService judgment)
{
    public async Task<PagedTasks> ListAsync(string? taskNo, string? status, Guid? planId, Guid? standardId,
        string? factory, string? line, string? inspector, DateTime? scheduledFrom, DateTime? scheduledTo,
        DateTime? submittedFrom, DateTime? submittedTo, string? assignee, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PatrolTasks.AsNoTracking().AsQueryable();
        if (assignee is not null) query = query.Where(x => x.AssignedInspectorKey == assignee);
        if (!string.IsNullOrWhiteSpace(taskNo)) query = query.Where(x => x.TaskNo.Contains(taskNo.Trim()));
        if (status == "Active") query = assignee is null ? query.Where(x => x.Status != "Completed") :
            query.Where(x => x.Status == "PendingInspection" || x.Status == "InProgress" || x.Status == "Rejected");
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (planId is { } pid) query = query.Where(x => x.PatrolPlanId == pid);
        if (standardId is { } sid) query = query.Where(x => x.PatrolStandardId == sid);
        if (!string.IsNullOrWhiteSpace(factory)) query = query.Where(x => x.FactoryNameSnapshot!.Contains(factory.Trim()));
        if (!string.IsNullOrWhiteSpace(line)) query = query.Where(x => x.LineNameSnapshot!.Contains(line.Trim()));
        if (!string.IsNullOrWhiteSpace(inspector)) query = query.Where(x => x.AssignedInspectorKey.Contains(inspector.Trim()));
        if (scheduledFrom is { } sf) query = query.Where(x => x.ScheduledOccurrenceUtc >= sf);
        if (scheduledTo is { } st) query = query.Where(x => x.ScheduledOccurrenceUtc <= st);
        if (submittedFrom is { } uf) query = query.Where(x => x.SubmittedAtUtc >= uf);
        if (submittedTo is { } ut) query = query.Where(x => x.SubmittedAtUtc <= ut);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.ScheduledOccurrenceUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(Summary).ToList(), page, pageSize, total);
    }

    public async Task<TaskDetail?> GetAsync(Guid id, CancellationToken ct)
    {
        var task = await db.PatrolTasks.AsNoTracking()
            .Include(x => x.Items).ThenInclude(x => x.Samples)
            .Include(x => x.Submissions).ThenInclude(x => x.Items).ThenInclude(x => x.Samples)
            .Include(x => x.Reviews)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (task is null) return null;
        var reviews = task.Reviews.ToDictionary(x => x.RevisionNo);
        return new TaskDetail(Summary(task), task.FactoryCodeSnapshot, task.WorkshopCodeSnapshot,
            task.LineCodeSnapshot, task.MaterialCodeSnapshot, task.SourceStandardUpdatedAtUtc,
            task.StartedAtUtc, task.CompletedAtUtc,
            task.Items.OrderBy(x => x.SequenceNo).Select(x => new TaskItemOutput(x.Id,
                x.SourcePatrolStandardItemId, x.SequenceNo, x.ProcessCode, x.ProcessName,
                x.InspectionItemCategory, x.InspectionItem, x.InspectionContent,
                x.UpperLimitOperator, x.UpperLimitValue, x.LowerLimitOperator, x.LowerLimitValue,
                x.InspectionType, x.SamplingPlan, x.SampleCount, x.PhotoRequirement, x.DefectLevel,
                x.IsNa, x.JudgmentResult, x.InspectedAtUtc, x.MachineCode, x.Series, x.Mold, x.AbnormalType,
                x.AbnormalCause, x.Remarks, x.Samples.OrderBy(s => s.SequenceNo)
                    .Select(s => new TaskSampleOutput(s.Id, s.SequenceNo, s.InspectionValue,
                        s.JudgmentResult, s.InspectedAtUtc)).ToList())).ToList(),
            task.Submissions.OrderBy(x => x.RevisionNo).Select(x => new SubmissionOutput(x.RevisionNo,
                x.Shift, x.OverallInspectionResult, x.SubmittedBy, x.SubmittedAtUtc,
                x.Items.OrderBy(i => i.SequenceNo).Select(i => new SubmissionItemOutput(i.PatrolTaskItemId,
                    i.SequenceNo, i.IsNa, i.JudgmentResult, i.InspectedAtUtc, i.MachineCode, i.Series, i.Mold,
                    i.AbnormalType, i.AbnormalCause, i.Remarks, i.Samples.OrderBy(s => s.SequenceNo)
                        .Select(s => new SubmissionSampleOutput(s.SequenceNo, s.InspectionValue,
                            s.JudgmentResult, s.InspectedAtUtc)).ToList())).ToList(),
                reviews.TryGetValue(x.RevisionNo, out var review) ? new ReviewOutput(review.RevisionNo,
                    review.Reviewer, review.Decision, review.Reason, review.ReviewedAtUtc) : null)).ToList());
    }

    public async Task<TaskDetail?> SaveDraftAsync(Guid id, string assignee, TaskDraftInput input, CancellationToken ct)
    {
        var task = await EditableTask(id, assignee, ct);
        if (task is null) return null;
        EnsureEditable(task);
        var errors = ValidateDraftInput(task, input);
        if (errors.Count > 0) throw new TaskValidationException(errors);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var draft in input.Items ?? [])
        {
            var item = task.Items.Single(x => x.Id == draft.Id);
            var previousIsNa = item.IsNa;
            item.IsNa = draft.IsNa;
            item.MachineCode = draft.MachineCode?.Trim();
            item.Series = draft.Series?.Trim();
            item.Mold = draft.Mold?.Trim();
            item.AbnormalType = draft.AbnormalType?.Trim();
            item.AbnormalCause = draft.AbnormalCause?.Trim();
            item.Remarks = draft.Remarks?.Trim();
            var submittedSamples = draft.IsNa == true ? [] : draft.Samples ?? [];
            for (var i = 0; i < submittedSamples.Count; i++)
            {
                var source = submittedSamples[i];
                var sample = item.Samples.SingleOrDefault(x => x.SequenceNo == i + 1);
                if (sample is null)
                {
                    sample = new PatrolTaskItemSample { Id = Guid.NewGuid(), SequenceNo = i + 1,
                        PatrolTaskItemId = item.Id };
                    item.Samples.Add(sample);
                    db.PatrolTaskItemSamples.Add(sample);
                }
                var inspectionType = InspectionJudgmentService.CanonicalType(item.InspectionType);
                var inspectionValue = inspectionType == "Quantitative" ? source.InspectionValue : null;
                var sampleJudgment = inspectionType == "Qualitative" ? source.JudgmentResult : null;
                var inputChanged = inspectionType == "Quantitative"
                    ? sample.InspectionValue != inspectionValue
                    : sample.JudgmentResult != sampleJudgment;
                sample.InspectionValue = inspectionValue;
                sample.JudgmentResult = sampleJudgment;
                if (inputChanged)
                    sample.InspectedAtUtc = inspectionValue is not null || sampleJudgment is not null ? now : null;
            }
            foreach (var old in item.Samples.Where(x => x.SequenceNo > submittedSamples.Count).ToList())
            {
                item.Samples.Remove(old);
                db.PatrolTaskItemSamples.Remove(old);
            }
            judgment.Calculate(task, strict: false);
            if (item.IsNa == true)
            {
                if (previousIsNa != true) item.InspectedAtUtc = now;
            }
            else
            {
                item.InspectedAtUtc = item.JudgmentResult is null ? null : item.Samples
                    .Where(x => x.InspectedAtUtc is not null)
                    .Select(x => x.InspectedAtUtc)
                    .OrderByDescending(x => x)
                    .FirstOrDefault();
            }
        }
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var claimed = await db.PatrolTasks.Where(x => x.Id == id && x.AssignedInspectorKey == assignee &&
                    x.Status == task.Status && x.CurrentRevisionNo == task.CurrentRevisionNo)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.Status,
                        task.Status == "PendingInspection" ? "InProgress" : task.Status)
                    .SetProperty(t => t.Shift, t => input.Shift ?? t.Shift)
                    .SetProperty(t => t.StartedAtUtc, t => t.StartedAtUtc ?? now)
                    .SetProperty(t => t.UpdatedAtUtc, now), ct);
            if (claimed != 1) throw new TaskWorkflowException("Task changed before draft save; reload it.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsWriteRace(ex))
        {
            throw new TaskWorkflowException("Task changed during draft save; reload it.");
        }
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetail?> SubmitAsync(Guid id, string assignee, CancellationToken ct)
    {
        var task = await EditableTask(id, assignee, ct);
        if (task is null) return null;
        EnsureEditable(task);
        if (task.PlanNoSnapshot is null || task.StandardNameSnapshot is null)
            throw new TaskWorkflowException("This development Task predates snapshots; reset local development task data.");
        var errors = judgment.Calculate(task, strict: true);
        foreach (var item in task.Items.Where(x => x.JudgmentResult is not null && x.InspectedAtUtc is null))
            errors[$"Items[{item.SequenceNo}].InspectedAtUtc"] = ["The completed item has no inspection time; save its current inspection again."];
        if (errors.Count > 0) throw new TaskValidationException(errors);
        var now = clock.GetUtcNow().UtcDateTime;
        var revision = task.CurrentRevisionNo + 1;
        var overall = InspectionJudgmentService.Overall(task);
        var submission = new PatrolTaskSubmission
        {
            Id = Guid.NewGuid(), PatrolTaskId = id, RevisionNo = revision, Shift = task.Shift!,
            OverallInspectionResult = overall, SubmittedBy = assignee, SubmittedAtUtc = now,
            Items = task.Items.OrderBy(x => x.SequenceNo).Select(x => new PatrolTaskSubmissionItem
            {
                Id = Guid.NewGuid(), PatrolTaskItemId = x.Id, SequenceNo = x.SequenceNo,
                IsNa = x.IsNa!.Value, JudgmentResult = x.JudgmentResult!, InspectedAtUtc = x.InspectedAtUtc!.Value,
                MachineCode = x.MachineCode, Series = x.Series, Mold = x.Mold,
                AbnormalType = x.AbnormalType, AbnormalCause = x.AbnormalCause, Remarks = x.Remarks,
                Samples = x.IsNa == true ? [] : x.Samples.OrderBy(s => s.SequenceNo)
                    .Select(s => new PatrolTaskSubmissionSample
                    {
                        Id = Guid.NewGuid(), SequenceNo = s.SequenceNo, InspectionValue = s.InspectionValue,
                        JudgmentResult = s.JudgmentResult!, InspectedAtUtc = s.InspectedAtUtc
                    }).ToList()
            }).ToList()
        };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var claimed = await db.PatrolTasks.Where(x => x.Id == id && x.AssignedInspectorKey == assignee &&
                    x.CurrentRevisionNo == task.CurrentRevisionNo &&
                    (x.Status == "PendingInspection" || x.Status == "InProgress" || x.Status == "Rejected"))
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, "PendingApproval")
                    .SetProperty(t => t.CurrentRevisionNo, revision)
                    .SetProperty(t => t.OverallInspectionResult, overall)
                    .SetProperty(t => t.SubmittedAtUtc, now)
                    .SetProperty(t => t.UpdatedAtUtc, now), ct);
            if (claimed != 1) throw new TaskWorkflowException("Task changed before submission; reload it.");
            db.PatrolTaskSubmissions.Add(submission);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsWriteRace(ex))
        {
            throw new TaskWorkflowException("Task changed during submission; reload it.");
        }
        return await GetAsync(id, ct);
    }

    private async Task<PatrolTask?> EditableTask(Guid id, string assignee, CancellationToken ct)
    {
        var task = await db.PatrolTasks.Include(x => x.Items).ThenInclude(x => x.Samples)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (task is not null && task.AssignedInspectorKey != assignee)
            throw new UnauthorizedAccessException("Task is assigned to another IPQA inspector.");
        return task;
    }

    private static void EnsureEditable(PatrolTask task)
    {
        if (task.Status is not ("PendingInspection" or "InProgress" or "Rejected"))
            throw new TaskWorkflowException("Task is not editable in its current status.");
    }

    private static Dictionary<string, string[]> ValidateDraftInput(PatrolTask task, TaskDraftInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Shift is not null && input.Shift is not ("Day" or "Night"))
            errors["Shift"] = ["Use Day or Night shift."];
        var drafts = input.Items ?? [];
        if (drafts.Count > task.Items.Count || drafts.Select(x => x.Id).Distinct().Count() != drafts.Count)
            errors["Items"] = ["Item IDs must be unique Task snapshot IDs."];
        foreach (var draft in drafts)
        {
            if (task.Items.All(x => x.Id != draft.Id)) errors[$"Items[{draft.Id}]"] = ["Item does not belong to this Task."];
            if ((draft.Samples?.Count ?? 0) > InspectionJudgmentService.MaxSamples)
                errors[$"Items[{draft.Id}].Samples"] = [$"No more than {InspectionJudgmentService.MaxSamples} samples are allowed."];
            if (draft.Samples?.Any(x => x.JudgmentResult is not (null or "OK" or "NG")) == true)
                errors[$"Items[{draft.Id}].Samples.JudgmentResult"] = ["Use OK or NG for Qualitative samples."];
            if (new[] { draft.MachineCode, draft.Series, draft.Mold, draft.AbnormalType,
                    draft.AbnormalCause, draft.Remarks }.Any(x => x?.Length > 2000))
                errors[$"Items[{draft.Id}].Metadata"] = ["Optional text must be 2000 characters or fewer."];
        }
        return errors;
    }

    private static TaskSummary Summary(PatrolTask task) => new(task.Id, task.TaskNo, task.Status,
        task.ScheduledOccurrenceUtc, task.GeneratedAtUtc, task.SubmittedAtUtc,
        task.OverallInspectionResult, task.AssignedInspectorKey, task.PlanNoSnapshot,
        task.PlanNameSnapshot, task.StandardNameSnapshot, task.FactoryNameSnapshot,
        task.LineNameSnapshot, task.Shift, task.CurrentRevisionNo);

    private static bool IsWriteRace(Exception ex) => ex is DbUpdateConcurrencyException ||
        ex is SqliteException { SqliteErrorCode: 5 or 6 } ||
        ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } ||
        ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqlite } &&
            sqlite.Message.Contains("PatrolTaskSubmissions.PatrolTaskId, PatrolTaskSubmissions.RevisionNo", StringComparison.OrdinalIgnoreCase) ||
        ex is DbUpdateException { InnerException: PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_PatrolTaskSubmissions_PatrolTaskId_RevisionNo" } };
}
