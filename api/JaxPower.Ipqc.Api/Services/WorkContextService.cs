using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace JaxPower.Ipqc.Api.Services;

public sealed class WorkContextSourceRevokedException(Guid contextId) : Exception("SourceSecurityRevoked")
{
    public Guid[] SourceContextIds { get; } = [contextId];
}

public sealed class WorkContextService(IpqcDbContext db, WorkContextStore store, WorkContextSecurity security,
    ConfiguredLineDirectory directory, TimeProvider clock)
{
    private static PersonalAccessException Conflict(string code = "SourceProvenanceConflict") => new(code, 409);
    private async Task<PatrolTask> Target(Guid id, CancellationToken ct)
    {
        var lines = await directory.LinesAsync(ct);
        var t = await db.PatrolTasks.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Samples)
            .Include(x => x.Submissions).Include(x => x.Reviews).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null || t.WorkflowVersion != WorkflowVersion.LineSharedV1 || lines.All(x => x.Id != t.LineId))
            throw new PersonalAccessException("NotFound", 404);
        return t;
    }
    private static void Scope(PatrolTask t, PersonalAuthority a, Guid line, int workflow, string environment)
    {
        if (line != t.LineId || workflow != (int)t.WorkflowVersion || environment != a.EnvironmentId)
            throw Conflict("WorkContextScopeMismatch");
        if (PlanningSnapshot.Status(t) != "ValidV1" || LineSharedTaskEligibility.Availability(t) != "FoundationOnly")
            throw Conflict("TaskReadContractUnavailable");
    }
    public Task<WorkContextIssued> IssueAsync(Guid taskId, WorkContextIssueInput input, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async a => {
            var t = await Target(taskId, ct); Scope(t, a, input.ExpectedTaskLineId, input.ExpectedWorkflowVersion, input.ExpectedEnvironmentId);
            var receipt = LinePatrolReadService.Receipt(a);
            var proof = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == a.UserId, ct);
            var c = new WorkContext { Id = Guid.NewGuid(), ProofSecretHash = Hash(proof), EnvironmentId = a.EnvironmentId,
                UserId = a.UserId, IssuedPersonalSessionId = a.PersonalSessionId, IssuedAuthVersion = user.AuthVersion,
                TaskId = t.Id, LineId = t.LineId!.Value, IssuedAtUtc = a.VerifiedAtUtc,
                CaptureUntilUtc = receipt.OfflineAccessUntilUtc, PersonalSessionExpiresAtUtc = a.PersonalSessionExpiresAtUtc };
            db.WorkContexts.Add(c); security.Audit(c, user.Id, user.Username, "ContextIssued");
            return new WorkContextIssued(Metadata(c), proof, receipt);
        }, ct);

    public Task<WorkContextPreflight> PreflightAsync(Guid taskId, WorkContextPreflightInput input, ClaimsPrincipal actor, CancellationToken ct)
    {
        // Shape is checked after authority even for direct service callers.
        var ids = input.Sources?.Take(101).Where(x => x is not null).Select(x => x.WorkContextId).ToArray() ?? [];
        return store.RunAsync(actor, false, taskId, ids, async a => {
            if (input.Sources is not { Count: >= 1 and <= 100 } || input.Sources.Any(x => x is null) || ids.Distinct().Count() != ids.Length)
                throw new PersonalAccessException("InvalidWorkContextSources", 400);
            var t = await Target(taskId, ct); Scope(t, a, input.ExpectedTaskLineId, input.ExpectedWorkflowVersion, input.ExpectedEnvironmentId);
            var result = new List<WorkContextValidated>();
            foreach (var s in input.Sources)
            {
                var c = await db.WorkContexts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == s.WorkContextId, ct);
                if (c is null || !ValidProof(s.ProofSecret, c.ProofSecretHash) || c.ContractVersion != 1 ||
                    c.UserId != s.ContributorUserIdAssertion || c.TaskId != taskId || c.LineId != t.LineId ||
                    c.WorkflowVersion != 2 || c.EnvironmentId != a.EnvironmentId || c.IssuedPersonalSessionId == Guid.Empty ||
                    c.IssuedAtUtc >= c.CaptureUntilUtc || c.CaptureUntilUtc > c.PersonalSessionExpiresAtUtc ||
                    c.CaptureUntilUtc - c.IssuedAtUtc > TimeSpan.FromHours(72)) throw Conflict();
                var owner = await db.ApplicationUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.UserId, ct);
                if (c.SecurityRevokedAtUtc is not null || owner is null || !owner.IsActive || owner.Role != "IPQA")
                    throw new WorkContextSourceRevokedException(c.Id);
                result.Add(new(c.Id, c.UserId, Utc(c.CaptureUntilUtc)));
            }
            return new WorkContextPreflight(clock.GetUtcNow().UtcDateTime, result);
        }, ct);
    }
    public Task<WorkContextMetadata> ReadAsync(Guid contextId, Guid? taskId, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, taskId is null, taskId, [contextId], async authority => {
            if (taskId is { } task) _ = await Target(task, ct);
            var c = await Context(contextId, ct);
            if (taskId is { } id && c.TaskId != id) throw new PersonalAccessException("NotFound", 404);
            return Metadata(c);
        }, ct);
    public Task<WorkContextMetadata> RevokeAsync(Guid contextId, WorkContextRevokeInput input, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, true, null, [contextId], async a => {
            if (input.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
                throw new PersonalAccessException("InvalidWorkContextRevocation", 400);
            var c = await Context(contextId, ct);
            if (c.Revision != input.ExpectedRevision) throw Conflict("WorkContextConflict");
            var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == a.UserId, ct);
            security.Revoke(c, user.Id, user.Username, input.Reason.Trim(), Guid.NewGuid());
            return Metadata(c);
        }, ct);
    public Task<object> AuditAsync(Guid id, int page, int size, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync<object>(actor, true, null, [id], async authority => {
            if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
                throw new PersonalAccessException("InvalidPagination", 400);
            await Context(id, ct);
            var q = db.WorkContextAuditEvents.AsNoTracking().Where(x => x.WorkContextId == id);
            var rows = await q.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new { Items = rows.Select(x => new WorkContextAuditMetadata(x.Id, x.ChangeId, x.WorkContextId, x.Action,
                Utc(x.OccurredAtUtc), x.ActorType, x.ActorUserId, x.ActorUsername, x.SourceUserId, x.Reason, x.BeforeRevision, x.AfterRevision,
                x.BeforeJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<WorkContextMetadata>(x.BeforeJson, WorkContextSecurity.SnapshotJson),
                System.Text.Json.JsonSerializer.Deserialize<WorkContextMetadata>(x.AfterJson, WorkContextSecurity.SnapshotJson)
                    ?? throw new InvalidOperationException("Missing WorkContext audit snapshot."))),
                Total = await q.CountAsync(ct), Page = page, PageSize = size };
        }, ct);
    private async Task<WorkContext> Context(Guid id, CancellationToken ct) => await db.WorkContexts.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new PersonalAccessException("NotFound", 404);
    private static string Hash(string proof) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof)));
    private static bool ValidProof(string? proof, string hash)
    {
        if (proof is not { Length: 43 } || proof.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try {
            var bytes = Base64UrlEncoder.DecodeBytes(proof);
            return bytes.Length == 32 && Base64UrlEncoder.Encode(bytes) == proof && hash.Length == 64 &&
                CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(proof)), Convert.FromHexString(hash));
        } catch (FormatException) { return false; }
    }
    private static DateTime Utc(DateTime v) => DateTime.SpecifyKind(v, DateTimeKind.Utc);
    public static WorkContextMetadata Metadata(WorkContext c) => new(c.Id, c.ContractVersion, c.EnvironmentId, c.UserId,
        c.IssuedPersonalSessionId, c.IssuedAuthVersion, c.TaskId, c.LineId, c.WorkflowVersion, Utc(c.IssuedAtUtc), Utc(c.CaptureUntilUtc),
        Utc(c.PersonalSessionExpiresAtUtc), c.SecurityRevokedAtUtc is { } revoked ? Utc(revoked) : null,
        c.SecurityRevocationReason, c.SecurityRevokedByUserId, c.Revision);
}
