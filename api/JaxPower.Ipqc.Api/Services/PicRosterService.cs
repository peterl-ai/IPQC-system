using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PicRosterService(IpqcDbContext db, WorkContextStore store, InspectionAttemptService identity, TimeProvider clock)
{
    private static PersonalAccessException Error(string code, int status = 409) => new(code, status);
    private static string Text(string? value) {
        if (value is null || value.Length > 400) throw Error("InvalidPicText", 400);
        for (var i = 0; i < value.Length;) {
            if (!Rune.TryGetRuneAt(value, i, out var rune) || Rune.IsControl(rune)) throw Error("InvalidPicText", 400);
            i += rune.Utf16SequenceLength;
        }
        var text = value.Trim();
        if (text.Length == 0 || text.EnumerateRunes().Count() > 200) throw Error("InvalidPicText", 400);
        return text;
    }
    private static void Page(int page, int size) {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue) throw Error("InvalidPicQuery", 400);
    }
    private static PicOutput Output(PicRosterRevision r) => new(r.PicId, r.Revision, r.Name, r.Department, r.IsActive);
    private static PicRevisionReceipt Receipt(PicRosterRevision r) => new(r.PicId, r.Revision, r.RequestId, r.ContractVersion,
        r.Action, r.Name, r.Department, r.IsActive, r.ActorUserId, r.ActorPersonalSessionId, r.ActorUsername, r.EnvironmentId,
        DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc));
    private IQueryable<PicRosterRevision> Current() => db.PicRosterRevisions.AsNoTracking().Where(r =>
        r.Revision == db.PicRosterRevisions.Where(p => p.PicId == r.PicId).Max(p => p.Revision));
    private async Task<PicPage<PicOutput>> List(bool? active, int page, int size, CancellationToken ct) {
        Page(page, size); var q = Current(); if (active is { } a) q = q.Where(r => r.IsActive == a);
        var rows = await q.OrderBy(x => x.PicId).Skip((page - 1) * size).Take(size + 1).ToListAsync(ct);
        return new(rows.Take(size).Select(Output).ToList(), page, size, rows.Count > size);
    }
    public Task<PicPage<PicOutput>> ListAsync(ClaimsPrincipal actor, bool? active, int page, int size, CancellationToken ct) =>
        store.RunAsync(actor, true, null, [], _ => List(active, page, size, ct), ct);
    public Task<PicOutput> GetAsync(Guid id, ClaimsPrincipal actor, CancellationToken ct) => store.RunAsync(actor, true, null, [], async _ =>
        Output(await Current().SingleOrDefaultAsync(x => x.PicId == id, ct) ?? throw Error("NotFound", 404)), ct);
    public Task<PicPage<PicRevisionReceipt>> HistoryAsync(Guid id, ClaimsPrincipal actor, int page, int size, CancellationToken ct) =>
        store.RunAsync(actor, true, null, [], async _ => {
            Page(page, size);
            if (!await db.PicRosterEntries.AnyAsync(x => x.PicId == id, ct)) throw Error("NotFound", 404);
            var rows = await db.PicRosterRevisions.AsNoTracking().Where(x => x.PicId == id).OrderByDescending(x => x.Revision)
                .Skip((page - 1) * size).Take(size + 1).ToListAsync(ct);
            return new PicPage<PicRevisionReceipt>(rows.Take(size).Select(Receipt).ToList(), page, size, rows.Count > size);
        }, ct);
    public Task<PicRevisionReceipt> RequestReceiptAsync(Guid requestId, ClaimsPrincipal actor, CancellationToken ct) =>
        store.RunAsync(actor, true, null, [], async _ => Receipt(await db.PicRosterRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RequestId == requestId, ct) ?? throw Error("NotFound", 404)), ct);
    public Task<PicLookupOutput> LookupAsync(Guid taskId, Guid? expectedLine, ClaimsPrincipal actor, int page, int size, CancellationToken ct) =>
        store.RunAsync(actor, false, taskId, [], async _ => {
            var task = await identity.Target(taskId, expectedLine, ct);
            return new PicLookupOutput(taskId, task.LineId!.Value, 2, await List(true, page, size, ct));
        }, ct);
    public Task<PicMutationResult> CreateAsync(PicCreateInput input, ClaimsPrincipal actor, CancellationToken ct) =>
        Mutate(null, input?.RequestId ?? Guid.Empty, 0, "Created", input?.Name, input?.Department, input?.IsActive, actor, ct);
    public Task<PicMutationResult> EditAsync(Guid id, PicEditInput input, ClaimsPrincipal actor, CancellationToken ct) =>
        Mutate(id, input?.RequestId ?? Guid.Empty, input?.ExpectedRevision ?? 0, "Updated", input?.Name, input?.Department, null, actor, ct);
    public Task<PicMutationResult> SetActiveAsync(Guid id, PicRevisionInput input, bool active, ClaimsPrincipal actor, CancellationToken ct) =>
        Mutate(id, input?.RequestId ?? Guid.Empty, input?.ExpectedRevision ?? 0, active ? "Activated" : "Deactivated", null, null, active, actor, ct);

    private async Task<PicMutationResult> Mutate(Guid? id, Guid request, long expected, string action, string? name, string? department,
        bool? active, ClaimsPrincipal actor, CancellationToken ct) {
        try {
            return await store.RunAsync(actor, true, null, [], async authority => {
                if (request == Guid.Empty || id == Guid.Empty || id is not null && expected < 1) throw Error("InvalidPicInput", 400);
                var n = action is "Created" or "Updated" ? Text(name) : null;
                var d = action is "Created" or "Updated" ? Text(department) : null;
                var hash = DraftCanonical.Hash(JsonSerializer.Serialize(new { domain = "pic-roster", v = 1,
                    environment = authority.EnvironmentId, request, pic = id, expected, action, name = n, department = d, active }, DraftCanonical.Json));
                var old = await db.PicRosterRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == request, ct);
                if (old is not null) {
                    if (old.RequestHash != hash) throw Error("PicRequestIdentityConflict");
                    return new PicMutationResult(Receipt(old), true);
                }
                PicRosterRevision? prior = null;
                if (id is { } pic) {
                    if (db.Database.IsNpgsql()) {
                        _ = await db.PicRosterEntries.FromSqlInterpolated($"SELECT * FROM \"PicRosterEntries\" WHERE \"PicId\"={pic} FOR UPDATE").AsNoTracking().ToListAsync(ct);
                        // Another Admin may have committed this exact request while this connection waited.
                        old = await db.PicRosterRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == request, ct);
                        if (old is not null) {
                            if (old.RequestHash != hash) throw Error("PicRequestIdentityConflict");
                            return new PicMutationResult(Receipt(old), true);
                        }
                    }
                    prior = await Current().SingleOrDefaultAsync(x => x.PicId == pic, ct) ?? throw Error("NotFound", 404);
                    if (prior.Revision != expected) throw Error("PicRevisionConflict");
                    if (expected == long.MaxValue) throw Error("PicRevisionExhausted");
                    if (action == "Updated" ? prior.Name == n && prior.Department == d : prior.IsActive == active) throw Error("PicNoChange");
                }
                var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
                var picId = id ?? Guid.NewGuid();
                if (id is null) { db.PicRosterEntries.Add(new() { PicId = picId, CreatedAtUtc = now }); await db.SaveChangesAsync(ct); }
                var user = await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == authority.UserId, ct);
                var row = new PicRosterRevision { PicId = picId, Revision = expected + 1, RequestId = request, RequestHash = hash, Action = action,
                    Name = n ?? prior!.Name, Department = d ?? prior!.Department, IsActive = active ?? prior!.IsActive,
                    ActorUserId = authority.UserId, ActorPersonalSessionId = authority.PersonalSessionId, ActorUsername = user.Username,
                    EnvironmentId = authority.EnvironmentId, OccurredAtUtc = now };
                db.PicRosterRevisions.Add(row); await db.SaveChangesAsync(ct);
                return new PicMutationResult(Receipt(row), false);
            }, ct);
        } catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
            ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 }) { throw Error("PicConflict"); }
    }
}
