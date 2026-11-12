using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class InspectionAttemptService(IpqcDbContext db, WorkContextStore store, ConfiguredLineDirectory directory,
    PersonalAccessOptions options, TimeProvider clock)
{
    internal IQueryable<PatrolTask> Tasks() => db.PatrolTasks
        .Include(x=>x.SharedDraft)
        .Include(x => x.Items).ThenInclude(x => x.Samples)
        .Include(x => x.Items).ThenInclude(x => x.InspectionAttempts)
        .Include(x => x.Items).ThenInclude(x => x.CurrentInspectionAttempt)
        .Include(x => x.Submissions).Include(x => x.Reviews).AsSplitQuery();

    public static string Category(PatrolTask t)
    {
        if (t.WorkflowVersion == WorkflowVersion.LegacyPersonalV1) return "LegacyPersonalV1";
        if (t.WorkflowVersion != WorkflowVersion.LineSharedV1) return "UnsupportedWorkflow";
        var status = PlanningSnapshot.Status(t);
        if (status == "HistoricalUnadmitted") return status;
        if (status != "ValidV1") return "InvalidPlanningSnapshot";
        var identity = InitialInspectionIdentity.State(t);
        if (identity is not ("Initialized" or "Uninitialized")) return identity;
        var availability=LineSharedTaskEligibility.Availability(t);
        if (availability=="DraftFoundation") return "SharedDraftInitialized";
        if (availability != "FoundationOnly") return "HistoricalInspectionData";
        return identity == "Initialized" ? "AlreadyInitialized" : "EligibleCleanFoundationOnly";
    }

    internal async Task<PatrolTask> Target(Guid taskId, Guid? expectedLine, CancellationToken ct)
    {
        var lines = await directory.LinesAsync(ct);
        var t = await Tasks().SingleOrDefaultAsync(x => x.Id == taskId, ct);
        if (t is null || t.WorkflowVersion != WorkflowVersion.LineSharedV1 || lines.All(x => x.Id != t.LineId))
            throw new PersonalAccessException("NotFound", 404);
        if (expectedLine is { } expected && expected != t.LineId)
            throw new PersonalAccessException("TaskLineMismatch", 409);
        return t;
    }

    public Task<InspectionIdentityOutput> ReadAsync(Guid taskId, Guid? expectedLine, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async _ => Output(await Target(taskId, expectedLine, ct)), ct);

    public Task<InitialInspectionAttemptOutput> ReadAttemptAsync(Guid taskId, Guid attemptId, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async _ => {
            var output = Output(await Target(taskId, null, ct));
            return output.Items.SingleOrDefault(x => x.InspectionAttemptId == attemptId)
                ?? throw new PersonalAccessException("NotFound", 404);
        }, ct);

    public static string? SnapshotHash(PatrolTask t) => t.PlanningSnapshotJson is null ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t.PlanningSnapshotJson)));

    // Maintenance metadata only. This observation grants no initialization authority;
    // initialization rechecks the exact frozen bytes, actual items and eligibility under locks.
    public Task<InspectionIdentityInventory> InventoryAsync(int page, int size, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, true, null, [], async _ => {
            if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
                throw new PersonalAccessException("InvalidPagination", 400);
            var configured = options.ConfiguredLines();
            var active = await db.ProductionLines.AsNoTracking().Where(x => x.IsActive && configured.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
            var total = await db.PatrolTasks.CountAsync(ct);
            var tasks = await Tasks().AsNoTracking().OrderBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new InspectionIdentityInventory(tasks.Select(t => {
                var category = Category(t);
                if (category is "EligibleCleanFoundationOnly" or "AlreadyInitialized") {
                    if (t.LineId is not { } line || !configured.Contains(line)) category = "OutOfScopeLine";
                    else if (!active.Contains(line) || active.Count != 2) category = "LineConfigurationUnavailable";
                }
                return new InspectionIdentityInventoryRow(t.Id, t.LineId, (int)t.WorkflowVersion, category, t.Items.Count, SnapshotHash(t));
            }).ToList(), page, size, total);
        }, ct);

    // Peter's 2026-11-12 decision: current Admin only, explicit reason, immutable creation audit.
    public Task<InspectionIdentityOutput> InitializeAsync(Guid taskId, InspectionIdentityInitializeInput input,
        ClaimsPrincipal actor, CancellationToken ct) => store.RunAsync(actor, true, taskId, [], async authority => {
            if (input.ExpectedTaskLineId == Guid.Empty || input.ExpectedWorkflowVersion != 2 ||
                input.ExpectedPlanningSnapshotHash is not { Length: 64 } ||
                string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
                throw new PersonalAccessException("InvalidInspectionInitialization", 400);
            var t = await Target(taskId, input.ExpectedTaskLineId, ct);
            if (input.ExpectedEnvironmentId != authority.EnvironmentId || SnapshotHash(t) != input.ExpectedPlanningSnapshotHash)
                throw new PersonalAccessException("InspectionInventoryConflict", 409);
            var category = Category(t);
            if (category == "AlreadyInitialized") return Output(t);
            if (category != "EligibleCleanFoundationOnly")
                throw new PersonalAccessException("InspectionInitializationUnavailable", 409);
            var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == authority.UserId, ct);
            var attempts = InitialInspectionIdentity.Create(t, LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime),
                user.Id, user.Username, authority.PersonalSessionId, input.Reason.Trim());
            // Nonempty GUID children of an already tracked item must explicitly be Added.
            db.InspectionAttempts.AddRange(attempts);
            db.InspectionAttemptApplicabilities.AddRange(t.Items.Select(x => x.CurrentInspectionAttempt!));
            return Output(t);
        }, ct);

    internal static InspectionIdentityOutput Output(PatrolTask t)
    {
        var category = Category(t);
        var state = category is "AlreadyInitialized" or "SharedDraftInitialized" ? "Initialized" : category == "EligibleCleanFoundationOnly" ? "Uninitialized" : category;
        return new(t.Id, t.LineId!.Value, (int)t.WorkflowVersion, state, state != "Initialized" ? [] :
            t.Items.OrderBy(x => x.SequenceNo).Select(x => {
                var a = x.InspectionAttempts.Single();
                return new InitialInspectionAttemptOutput(a.Id, t.Id, x.Id, t.LineId.Value, (int)t.WorkflowVersion,
                    a.Kind, DateTime.SpecifyKind(a.CreatedAtUtc, DateTimeKind.Utc), a.CreationSource,
                    a.PlanningSnapshotVersion, a.MinimumSampleCount, a.FrozenSampleCount, true);
            }).ToList());
    }
}
