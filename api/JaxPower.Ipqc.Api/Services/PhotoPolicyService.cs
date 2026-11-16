using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PhotoPolicyService(IpqcDbContext db, LineSharedPlanningStore store, PersonalAccess access, TimeProvider clock)
{
    private static PersonalAccessException Error(string code, int status = 409) => new(code, status);
    public static string Definition(PatrolStandard s, PatrolStandardItem i) => Canonical(s.Id, i.Id, s.PatrolStandardName, s.FactoryCode, s.FactoryName, s.WorkshopCode, s.MaterialCode,
        i.SequenceNo, i.ProcessCode, i.ProcessName, i.InspectionItemCategory, i.InspectionItem, i.InspectionContent, i.UpperLimitOperator, i.UpperLimitValue,
        i.LowerLimitOperator, i.LowerLimitValue, i.InspectionType, i.SamplingPlan, i.SampleCount, i.PhotoRequirement, i.DefectLevel);
    private static string FrozenDefinition(PatrolTask t, PatrolTaskItem i) => Canonical(t.PatrolStandardId, i.SourcePatrolStandardItemId,
        t.StandardNameSnapshot!, t.FactoryCodeSnapshot!, t.FactoryNameSnapshot!, t.WorkshopCodeSnapshot!, t.MaterialCodeSnapshot!,
        i.SequenceNo, i.ProcessCode, i.ProcessName, i.InspectionItemCategory, i.InspectionItem, i.InspectionContent, i.UpperLimitOperator, i.UpperLimitValue,
        i.LowerLimitOperator, i.LowerLimitValue, i.InspectionType, i.SamplingPlan, i.SampleCount, i.PhotoRequirement, i.DefectLevel);
    private static string Canonical(Guid standard, Guid item, string name, string factoryCode, string factoryName, string workshop, string material,
        int sequence, params string[] fields) => JsonSerializer.Serialize(new { domain = "photo-policy-definition", v = 1,
            standard = DraftCanonical.Key(standard), item = DraftCanonical.Key(item), name, factoryCode, factoryName, workshop, material, sequence, fields }, DraftCanonical.Json);
    private static T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json, DraftCanonical.Json) ?? throw new JsonException();
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, DraftCanonical.Json);
    private static PhotoPolicyReceipt? ValidReceipt(PhotoPolicyConfirmation c, PhotoPolicyDefinition d) {
        try {
            var r = Decode<PhotoPolicyReceipt>(c.ReceiptJson);
            return r.ConfirmationId == c.Id && r.RequestId == c.RequestId && r.StandardId == c.StandardId && r.StandardItemId == c.StandardItemId &&
                r.DefinitionId == d.Id && r.DefinitionVersion == d.Version && r.CanonicalizationVersion == 1 && r.DefinitionHash == d.DefinitionHash &&
                d.DefinitionHash == DraftCanonical.Hash(d.DefinitionJson) && r.PolicyVersion == c.PolicyVersion && r.Value == c.Value && r.Value is "Required" or "Optional" &&
                r.ConfirmedByUserId == c.ActorUserId && r.ConfirmedPersonalSessionId == c.ActorPersonalSessionId && r.EnvironmentId == c.EnvironmentId &&
                r.ConfirmedAtUtc == c.ConfirmedAtUtc ? r : null;
        } catch (JsonException) { return null; }
    }
    private async Task<PatrolStandard> Standard(Guid id, CancellationToken ct) => await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems)
        .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("NotFound", 404);

    // Call only inside the existing planning writer unit of work, after authoritative Standard edits are flushed.
    public async Task SynchronizeAsync(PatrolStandard standard, bool deleted, CancellationToken ct)
    {
        foreach (var state in await db.PhotoPolicyStates.Where(x => x.StandardId == standard.Id).ToListAsync(ct)) {
            var item = deleted ? null : standard.InspectionItems.SingleOrDefault(x => x.Id == state.StandardItemId);
            if (item is null) { state.IsPresent = false; state.ConfirmationId = null; continue; }
            await EnsureDefinition(standard, item, state, ct);
        }
    }
    private async Task<(PhotoPolicyState State, bool NewEpoch)> EnsureDefinition(PatrolStandard s, PatrolStandardItem i, PhotoPolicyState? state, CancellationToken ct)
    {
        var json = Definition(s, i); var hash = DraftCanonical.Hash(json);
        var prior = state is null ? null : await db.PhotoPolicyDefinitions.AsNoTracking().SingleAsync(x => x.Id == state.DefinitionId, ct);
        if (state is { IsPresent: true } && prior!.DefinitionHash == hash && prior.DefinitionJson == json) return (state, false);
        var definition = new PhotoPolicyDefinition { Id = Guid.NewGuid(), StandardId = s.Id, StandardItemId = i.Id,
            Version = checked((prior?.Version ?? 0) + 1), DefinitionHash = hash, DefinitionJson = json, CreatedAtUtc = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime) };
        db.PhotoPolicyDefinitions.Add(definition); await db.SaveChangesAsync(ct);
        if (state is null) { state = new() { StandardId = s.Id, StandardItemId = i.Id }; db.PhotoPolicyStates.Add(state); }
        state.DefinitionId = definition.Id; state.IsPresent = true; state.ConfirmationId = null; await db.SaveChangesAsync(ct);
        return (state, true);
    }
    public Task<PhotoPolicyPage> ReadAsync(Guid standardId, ClaimsPrincipal actor, CancellationToken ct) => store.WriteAsync(actor, async () => {
        var standard = await Standard(standardId, ct); var items = new List<PhotoPolicyItem>();
        foreach (var item in standard.InspectionItems.OrderBy(x => x.SequenceNo)) {
            var json = Definition(standard, item); var hash = DraftCanonical.Hash(json);
            var state = await db.PhotoPolicyStates.AsNoTracking().SingleOrDefaultAsync(x => x.StandardItemId == item.Id, ct);
            var d = state is null ? null : await db.PhotoPolicyDefinitions.AsNoTracking().SingleAsync(x => x.Id == state.DefinitionId, ct);
            var current = state is { IsPresent: true } && d?.DefinitionHash == hash && d.DefinitionJson == json;
            var receipt = current && state!.ConfirmationId is { } id ? ValidReceipt(await db.PhotoPolicyConfirmations.AsNoTracking().SingleAsync(x => x.Id == id, ct), d!) : null;
            items.Add(new(standardId, item.Id, item.PhotoRequirement, current ? d!.Id : null, current ? d!.Version : null, hash,
                receipt is not null ? "ExplicitlyConfirmed" : current && state!.ConfirmationId is not null ? "InvalidConfiguration" : "Unconfirmed", receipt));
        }
        // Review text and exact confirmation preconditions must describe the same locked source read.
        return new PhotoPolicyPage(standardId, items, PatrolStandardService.ToOutput(standard));
    }, ct);
    public Task<PhotoPolicyReceipt> ConfirmAsync(Guid standardId, Guid itemId, PhotoPolicyConfirmInput input, ClaimsPrincipal actor, CancellationToken ct) => store.WriteAsync(actor, async () => {
        var authority = await access.ValidateAsync(actor, false, ct);
        if ((await db.ApplicationUsers.AsNoTracking().SingleAsync(x => x.Id == authority.UserId, ct)).Role != "PQE") throw Error("PhotoPolicyConfirmationForbidden", 403);
        if (standardId == Guid.Empty || itemId == Guid.Empty || input.RequestId == Guid.Empty || input.CanonicalizationVersion != 1 ||
            !DraftCanonical.IsHash(input.ExpectedDefinitionHash) || input.ExpectedDefinitionId == Guid.Empty || input.ExpectedConfirmationId == Guid.Empty ||
            input.Value is not ("Required" or "Optional")) throw Error("InvalidPhotoPolicyConfirmation", 400);
        var fingerprint = DraftCanonical.Hash(Encode(new { domain = "photo-policy-confirmation", v = 1, standardId, itemId, input.RequestId,
            input.ExpectedDefinitionId, hash = input.ExpectedDefinitionHash.ToUpperInvariant(), input.ExpectedConfirmationId, input.Value }));
        var old = await db.PhotoPolicyConfirmations.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == input.RequestId, ct);
        if (old is not null) { if (old.RequestHash != fingerprint) throw Error("PhotoPolicyRequestIdentityConflict"); return Decode<PhotoPolicyReceipt>(old.ReceiptJson); }
        var standard = await Standard(standardId, ct); var item = standard.InspectionItems.SingleOrDefault(x => x.Id == itemId) ?? throw Error("NotFound", 404);
        // Confirming this item's photo requirement does not approve its measurement definition or another item's readiness.
        // The generator independently retains every existing Ready/admission check.
        if (DraftCanonical.Hash(Definition(standard, item)) != input.ExpectedDefinitionHash.ToUpperInvariant()) throw Error("PhotoPolicyDefinitionConflict");
        var previous = await db.PhotoPolicyStates.SingleOrDefaultAsync(x => x.StandardItemId == itemId, ct);
        var (state, newEpoch) = await EnsureDefinition(standard, item, previous, ct);
        if (input.ExpectedDefinitionId != (newEpoch ? null : state.DefinitionId) || input.ExpectedConfirmationId != state.ConfirmationId) throw Error("PhotoPolicyConfirmationConflict");
        var definition = await db.PhotoPolicyDefinitions.AsNoTracking().SingleAsync(x => x.Id == state.DefinitionId, ct);
        var version = checked(state.PolicyVersion + 1); var now = LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
        var receipt = new PhotoPolicyReceipt(Guid.NewGuid(), input.RequestId, standardId, itemId, definition.Id, definition.Version, 1,
            definition.DefinitionHash, version, input.Value, authority.UserId, authority.PersonalSessionId, authority.EnvironmentId, now);
        db.PhotoPolicyConfirmations.Add(new() { Id = receipt.ConfirmationId, RequestId = input.RequestId, StandardId = standardId, StandardItemId = itemId,
            DefinitionId = definition.Id, PolicyVersion = version, ExpectedConfirmationId = input.ExpectedConfirmationId, Value = input.Value,
            ActorUserId = authority.UserId, ActorPersonalSessionId = authority.PersonalSessionId, EnvironmentId = authority.EnvironmentId,
            RequestHash = fingerprint, ReceiptJson = Encode(receipt), ConfirmedAtUtc = now }); await db.SaveChangesAsync(ct);
        state.ConfirmationId = receipt.ConfirmationId; state.PolicyVersion = version;
        return receipt;
    }, ct);
    private async Task<PhotoPolicyReceipt> Receipt(Guid id, CancellationToken ct) {
        var c = await db.PhotoPolicyConfirmations.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var d = await db.PhotoPolicyDefinitions.AsNoTracking().SingleAsync(x => x.Id == c.DefinitionId, ct);
        return ValidReceipt(c, d) ?? throw Error("PhotoPolicyReceiptInvalid");
    }
    public Task<PhotoPolicyReceipt> ReadReceiptAsync(Guid id, ClaimsPrincipal actor, CancellationToken ct) => store.WriteAsync(actor, async () => {
        if (!await db.PhotoPolicyConfirmations.AnyAsync(x => x.Id == id, ct)) throw Error("NotFound", 404); return await Receipt(id, ct);
    }, ct);
    public async Task<Dictionary<Guid, PhotoPolicyReceipt>?> EffectiveAsync(PatrolStandard standard, CancellationToken ct)
    {
        var result = new Dictionary<Guid, PhotoPolicyReceipt>();
        foreach (var item in standard.InspectionItems) {
            var state = await db.PhotoPolicyStates.AsNoTracking().SingleOrDefaultAsync(x => x.StandardItemId == item.Id, ct);
            if (state is not { IsPresent: true, ConfirmationId: not null } || state.StandardId != standard.Id) return null;
            var definition = await db.PhotoPolicyDefinitions.AsNoTracking().SingleAsync(x => x.Id == state.DefinitionId, ct);
            var json = Definition(standard, item);
            if (definition.DefinitionJson != json || definition.DefinitionHash != DraftCanonical.Hash(json)) return null;
            var receipt = ValidReceipt(await db.PhotoPolicyConfirmations.AsNoTracking().SingleAsync(x => x.Id == state.ConfirmationId.Value, ct), definition);
            if (receipt is null || receipt.DefinitionId != definition.Id || receipt.StandardId != standard.Id || receipt.StandardItemId != item.Id ||
                receipt.PolicyVersion != state.PolicyVersion || receipt.Value is not ("Required" or "Optional")) return null;
            result.Add(item.Id, receipt);
        }
        return result;
    }
    public void Freeze(PatrolTask task, PatrolPlan plan, Dictionary<Guid, PhotoPolicyReceipt> policies)
    {
        foreach (var item in task.Items) {
            var snapshot = new FrozenPhotoPolicy(task.Id, item.Id, plan.Id, plan.SharedPlanRevision!.Value, policies[item.SourcePatrolStandardItemId]);
            var json = Encode(snapshot); db.FrozenTaskPhotoPolicies.Add(new() { TaskId = task.Id, TaskItemId = item.Id,
                ConfirmationId = snapshot.Policy.ConfirmationId, SnapshotJson = json, SnapshotHash = DraftCanonical.Hash(json) });
        }
    }
    public Task<TaskPhotoPolicyItem> FrozenAsync(PatrolTask task, PatrolTaskItem item, CancellationToken ct) =>
        ReadFrozenAsync(task, item, PlanningSnapshot.Read(task), ct);
    internal async Task<TaskPhotoPolicyItem> ReadFrozenAsync(PatrolTask task, PatrolTaskItem item, PlanningDefinitionV1? planning, CancellationToken ct)
    {
        var row = await db.FrozenTaskPhotoPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.TaskItemId == item.Id, ct);
        if (row is null) return new(item.Id, "Unconfirmed", null);
        try {
            var f = Decode<FrozenPhotoPolicy>(row.SnapshotJson); var r = f.Policy;
            var c = await db.PhotoPolicyConfirmations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.ConfirmationId, ct);
            var d = c is null ? null : await db.PhotoPolicyDefinitions.AsNoTracking().SingleAsync(x => x.Id == c.DefinitionId, ct);
            if (planning is null || f.ContractVersion != 1 || row.TaskId != task.Id || row.SnapshotHash != DraftCanonical.Hash(row.SnapshotJson) || f.TaskId != task.Id || f.TaskItemId != item.Id ||
                f.PlanId != task.PatrolPlanId || f.SourcePlanRevision != planning.SourcePlanRevision || c is null || r != ValidReceipt(c, d!) ||
                r.ConfirmationId != row.ConfirmationId || r.StandardId != task.PatrolStandardId || r.StandardItemId != item.SourcePatrolStandardItemId ||
                r.Value is not ("Required" or "Optional") || d!.DefinitionJson != FrozenDefinition(task, item) || d.DefinitionHash != DraftCanonical.Hash(d.DefinitionJson))
                return new(item.Id, "InvalidConfiguration", null);
            return new(item.Id, "ExplicitlyConfirmed", f);
        } catch (Exception ex) when (ex is JsonException or NullReferenceException or ArgumentException) { return new(item.Id, "InvalidConfiguration", null); }
    }
}
