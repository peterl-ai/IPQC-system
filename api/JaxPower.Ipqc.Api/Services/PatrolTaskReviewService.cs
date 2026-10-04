using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PatrolTaskReviewService(IpqcDbContext db, TimeProvider clock)
{
    public Task ApproveAsync(Guid id, string reviewer, CancellationToken ct) =>
        DecideAsync([id], reviewer, "Approved", null, ct);

    public Task RejectAsync(Guid id, string reviewer, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new TaskValidationException(new() { ["Reason"] = ["A rejection reason is required."] });
        if (reason.Trim().Length > 2000)
            throw new TaskValidationException(new() { ["Reason"] = ["Reason must be 2000 characters or fewer."] });
        return DecideAsync([id], reviewer, "Rejected", reason.Trim(), ct);
    }

    public Task BatchApproveAsync(IReadOnlyList<Guid>? ids, string reviewer, CancellationToken ct)
    {
        if (ids is null || ids.Count is < 1 or > 50 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw new TaskValidationException(new() { ["TaskIds"] = ["Select 1–50 distinct Tasks."] });
        return DecideAsync(ids, reviewer, "Approved", null, ct);
    }

    private async Task DecideAsync(IReadOnlyList<Guid> ids, string reviewer, string decision, string? reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var tasks = await db.PatrolTasks.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
            if (tasks.Count != ids.Count) throw new KeyNotFoundException("Patrol Task not found.");
            if (tasks.Any(x => x.Status != "PendingApproval" || x.CurrentRevisionNo < 1))
                throw new TaskWorkflowException("All selected Tasks must be Pending Approval.");
            foreach (var task in tasks)
            {
                var updated = await db.PatrolTasks.Where(x => x.Id == task.Id && x.Status == "PendingApproval" &&
                        x.CurrentRevisionNo == task.CurrentRevisionNo)
                    .ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, decision == "Approved" ? "Completed" : "Rejected")
                        .SetProperty(t => t.CompletedAtUtc, decision == "Approved" ? now : (DateTime?)null)
                        .SetProperty(t => t.UpdatedAtUtc, now), ct);
                if (updated != 1) throw new TaskWorkflowException("A Task changed during review; reload and retry.");
                db.PatrolTaskReviews.Add(new PatrolTaskReview
                {
                    Id = Guid.NewGuid(), PatrolTaskId = task.Id, RevisionNo = task.CurrentRevisionNo,
                    Reviewer = reviewer, Decision = decision, Reason = reason, ReviewedAtUtc = now
                });
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsReviewRace(ex))
        {
            throw new TaskWorkflowException("This submission was already reviewed.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new TaskWorkflowException("A competing review is in progress; reload and retry.");
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new TaskWorkflowException("A competing review is in progress; reload and retry.");
        }
    }

    private static bool IsReviewRace(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqlite &&
            sqlite.Message.Contains("PatrolTaskReviews.PatrolTaskId, PatrolTaskReviews.RevisionNo", StringComparison.OrdinalIgnoreCase) ||
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_PatrolTaskReviews_PatrolTaskId_RevisionNo"
        };
}
