using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

// Called only within the caller's account/context transaction; never opens a transaction.
public sealed class WorkContextSecurity(IpqcDbContext db, TimeProvider clock)
{
    public async Task RevokeUserAsync(Guid userId, Guid actorId, string actor, string reason, CancellationToken ct)
    {
        var contexts = await db.WorkContexts.Where(x => x.UserId == userId && x.SecurityRevokedAtUtc == null)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var change = Guid.NewGuid();
        foreach (var context in contexts) Revoke(context, actorId, actor, reason, change);
    }

    public void Revoke(WorkContext c, Guid actorId, string actor, string reason, Guid change)
    {
        if (c.SecurityRevokedAtUtc is not null) return;
        var before = c.Revision;
        c.Revision = checked(c.Revision + 1);
        c.SecurityRevokedAtUtc = clock.GetUtcNow().UtcDateTime;
        c.SecurityRevocationReason = reason;
        c.SecurityRevokedByUserId = actorId;
        Audit(c, actorId, actor, "ContextSecurityRevoked", reason, before, change);
    }

    public void Audit(WorkContext c, Guid actorId, string actor, string action, string? reason = null,
        long? before = null, Guid? change = null) => db.WorkContextAuditEvents.Add(new() {
            Id = Guid.NewGuid(), ChangeId = change ?? Guid.NewGuid(), WorkContextId = c.Id,
            Action = action, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, ActorUserId = actorId,
            ActorUsername = actor, SourceUserId = c.UserId, Reason = reason,
            BeforeRevision = before, AfterRevision = c.Revision });
}
