using System.Security.Claims;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class DraftQuarantineException(string code, Guid[] operations, Guid[] contexts) : Exception(code)
{
    public Guid[] OperationIds { get; } = operations;
    public Guid[] SourceContextIds { get; } = contexts;
}

public sealed partial class SharedDraftService(IpqcDbContext db, WorkContextStore store, InspectionAttemptService identity, TimeProvider clock)
{
    private static PersonalAccessException Error(string code,int status=409)=>new(code,status);
    private static T Decode<T>(string json)=>JsonSerializer.Deserialize<T>(json,DraftCanonical.Json) ?? throw new InvalidOperationException("Missing accepted receipt.");
    private static string Encode<T>(T data)=>JsonSerializer.Serialize(data,DraftCanonical.Json);
    private static DraftOperationReceipt Receipt(ContributionOperation op)=>Decode<DraftOperationReceipt>(op.ReceiptJson);
    private static bool Current(PatrolTask task,Guid itemId,Guid attemptId)=>task.Items.Any(i=>i.Id==itemId &&
        i.CurrentInspectionAttempt?.InspectionAttemptId==attemptId && i.InspectionAttempts.Any(a=>a.Id==attemptId));

    public Task<DraftBatchResult> AcceptAsync(Guid taskId,DraftBatchInput input,ClaimsPrincipal actor,CancellationToken ct)
    {
        // Snapshot all request-owned collections before the store can await locks.
        // Source-owner discovery and acceptance must use the very same manifest.
        // Preserve malformed/null members for authenticated Shape validation.
        var b=input with {
            Operations=input.Operations?.Select(o=>o is null ? o! : o with { Dependencies=o.Dependencies?.ToList()! }).ToList()!,
            AcceptedReferences=input.AcceptedReferences?.ToList()!,SourceProofs=input.SourceProofs?.ToList()!
        };
        var sources=SourceIds(b);
        return store.RunAsync(actor,false,taskId,sources,async authority=> {
            Shape(b);
            var task=await identity.Target(taskId,b.ExpectedTaskLineId,ct);
            if(b.ExpectedWorkflowVersion!=(int)task.WorkflowVersion || b.ExpectedEnvironmentId!=authority.EnvironmentId)
                throw Error("DraftScopeMismatch");
            var batch=await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.TaskId==taskId && x.BatchId==b.BatchId,ct);
            if(b.CanonicalizationVersion!=1 || b.Operations.Any(x=>x.CanonicalizationVersion!=1)) {
                if(batch is not null) throw Error("BatchIdentityConflict");
                var changedVersions=b.Operations.Where(x=>x.CanonicalizationVersion!=1).Select(x=>x.OperationId).ToArray();
                if(await db.ContributionOperations.AnyAsync(x=>x.TaskId==taskId && changedVersions.Contains(x.OperationId),ct)) throw Error("OperationIdentityConflict");
                throw Error("UnsupportedOperationVersion",400);
            }
            var payloads=b.Operations.ToDictionary(o=>o.OperationId,o=>DraftCanonical.Operation(taskId,b,o));
            var hashes=payloads.ToDictionary(x=>x.Key,x=>DraftCanonical.Hash(x.Value));
            var fingerprint=DraftCanonical.Batch(taskId,b,hashes);
            if(batch is not null) {
                if(batch.RequestFingerprint!=fingerprint) throw Error("BatchIdentityConflict");
                var original=Decode<DraftBatchReceipt>(batch.ReceiptJson);
                return new DraftBatchResult(original,original.Operations,true);
            }
            var requested=b.Operations.Select(x=>x.OperationId).Concat(b.AcceptedReferences.Select(x=>x.OperationId)).ToArray();
            if(await db.SharedOperationIdentities.AnyAsync(x=>x.TaskId==taskId && requested.Contains(x.OperationId) && x.Domain!="Sample",ct))
                throw Error("OperationIdentityConflict");
            var known=await db.ContributionOperations.AsNoTracking().Where(x=>x.TaskId==taskId && requested.Contains(x.OperationId)).ToDictionaryAsync(x=>x.OperationId,ct);
            foreach(var o in b.Operations)
                if(known.TryGetValue(o.OperationId,out var old) && old.ContentHash!=hashes[o.OperationId]) throw Error("OperationIdentityConflict");
            foreach(var r in b.AcceptedReferences)
                if(!known.TryGetValue(r.OperationId,out var old) || old.ContentHash!=r.ContentHash.ToUpperInvariant()) throw Error("AcceptedReferenceConflict");
            var fresh=b.Operations.Where(x=>!known.ContainsKey(x.OperationId)).ToDictionary(x=>x.OperationId);
            if(fresh.Count==0) return new DraftBatchResult(null,known.Values.OrderBy(x=>DraftCanonical.Key(x.OperationId),StringComparer.Ordinal).Select(Receipt).ToList(),true);
            if(PlanningSnapshot.Status(task)!="ValidV1" || InitialInspectionIdentity.State(task)!="Initialized" ||
                LineSharedTaskEligibility.Availability(task) is not ("FoundationOnly" or "DraftFoundation")) throw Error("DraftAcceptanceUnavailable");
            if(fresh.Values.Any(o=>o.BaseDraftVersion!=b.ExpectedDraftVersion || !Current(task,o.TaskItemId,o.InspectionAttemptId))) throw Error("DraftAttemptConflict");
            var freshAttempts=fresh.Values.Select(x=>x.InspectionAttemptId).ToArray();
            if(await db.ItemRemovalOperations.AnyAsync(x=>x.TaskId==taskId && freshAttempts.Contains(x.InspectionAttemptId),ct)) throw Error("DraftItemRemoved");
            var ordered=Manifest(b,fresh,known,hashes);
            // Accepted facts are resolved before source checks; no proof/current-A test is applied to them.
            foreach(var k in known.Values)
                if(k.EnvironmentId!=authority.EnvironmentId || k.LineId!=task.LineId || k.WorkflowVersion!=2 ||
                    !Current(task,k.TaskItemId,k.InspectionAttemptId)) throw Error("AcceptedAttemptNotApplicable");
            var owners=await Sources(task,b,ordered,authority,ct);
            var samples=await db.SharedSamples.AsNoTracking().Where(x=>x.TaskId==taskId).ToListAsync(ct);
            var projections=await db.SharedSampleProjections.AsNoTracking().Where(x=>x.TaskId==taskId).ToDictionaryAsync(x=>x.SampleId,ct);
            var existingProjectionIds=projections.Keys.ToHashSet(); var newSamples=new List<SharedSample>();
            var receipts=known.ToDictionary(x=>x.Key,x=>Receipt(x.Value)); var newReceipts=new List<DraftOperationReceipt>();
            var now=LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
            if(b.ExpectedDraftVersion==long.MaxValue) throw Error("DraftVersionExhausted");
            var next=b.ExpectedDraftVersion+1;
            foreach(var op in ordered) {
                if(op.Kind is not ("CaptureSample" or "CorrectSample" or "RemoveSample")) throw Error("UnsupportedSampleOperation",400);
                var sample=samples.SingleOrDefault(x=>x.InspectionAttemptId==op.InspectionAttemptId && x.ClientSampleId==op.ClientSampleId);
                SharedSampleProjection projection;
                DraftSampleValue? value=null;
                if(op.Kind=="CaptureSample") {
                    if(sample is not null || op.Dependencies.Count!=0) throw Error("SampleIdentityConflict");
                    value=Value(task.Items.Single(x=>x.Id==op.TaskItemId),op.Data);
                    var last=samples.Where(x=>x.InspectionAttemptId==op.InspectionAttemptId).Select(x=>x.SequenceNo).DefaultIfEmpty(0).Max();
                    if(last==int.MaxValue) throw Error("SampleSequenceExhausted");
                    sample=new SharedSample { Id=Guid.NewGuid(),TaskId=taskId,TaskItemId=op.TaskItemId,InspectionAttemptId=op.InspectionAttemptId,
                        ClientSampleId=op.ClientSampleId,SequenceNo=last+1 };
                    samples.Add(sample);newSamples.Add(sample);
                    projection=new SharedSampleProjection { SampleId=sample.Id,TaskId=taskId,ValueOperationId=op.OperationId };
                    projections.Add(sample.Id,projection);
                } else {
                    if(sample is null || sample.TaskItemId!=op.TaskItemId || !projections.TryGetValue(sample.Id,out projection!) || projection.IsRemoved || op.Dependencies.Count!=1)
                        throw Error("SampleMutationConflict");
                    var expected=op.Dependencies[0];
                    if(projection.LastOperationId!=expected.OperationId || !receipts.TryGetValue(expected.OperationId,out var predecessor) ||
                        predecessor.SampleId!=sample.Id || predecessor.ContentHash!=expected.ContentHash.ToUpperInvariant()) throw Error("SamplePredecessorConflict");
                    if(op.Kind=="RemoveSample") {
                        if(op.Data is not null) throw Error("InvalidSampleRemoval",400);
                        projection.IsRemoved=true;
                    } else { value=Value(task.Items.Single(x=>x.Id==op.TaskItemId),op.Data);projection.ValueOperationId=op.OperationId; }
                }
                if(value is not null) { projection.InspectionType=value.InspectionType;projection.InspectionValue=value.InspectionValue;projection.Judgment=value.Judgment;projection.Barcode=value.Barcode; }
                projection.LastOperationId=op.OperationId;
                var c=owners[op.SourceWorkContextId];
                var receipt=new DraftOperationReceipt(taskId,op.OperationId,b.BatchId,1,hashes[op.OperationId],op.Kind,op.TaskItemId,op.InspectionAttemptId,
                    sample.Id,op.ClientSampleId,c.Id,c.UserId,authority.UserId,authority.PersonalSessionId,authority.EnvironmentId,task.LineId!.Value,2,now,next,
                    value,projection.IsRemoved,projection.ValueOperationId);
                receipts.Add(op.OperationId,receipt);newReceipts.Add(receipt);
            }
            if(samples.GroupBy(x=>x.InspectionAttemptId).Any(g=>g.Count(x=>!projections[x.Id].IsRemoved)>50)) throw Error("SharedSampleLimitExceeded",400);
            var root=await db.SharedDrafts.SingleOrDefaultAsync(x=>x.TaskId==taskId,ct);
            if(root is null) { root=new SharedDraft { TaskId=taskId }; db.SharedDrafts.Add(root);await db.SaveChangesAsync(ct); }
            if(await db.SharedDrafts.Where(x=>x.TaskId==taskId && x.DraftVersion==b.ExpectedDraftVersion && x.DraftVersion<long.MaxValue)
                .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.DraftVersion,x=>x.DraftVersion+1),ct)!=1) throw Error("DraftVersionConflict");
            await db.Entry(root).ReloadAsync(ct);
            // All intermediate saves are inside WorkContextStore's same transaction.
            db.SharedSamples.AddRange(newSamples);await db.SaveChangesAsync(ct);
            var all=receipts.Values.OrderBy(x=>DraftCanonical.Key(x.OperationId),StringComparer.Ordinal).ToList();
            var outcome=new DraftBatchReceipt(taskId,b.BatchId,fingerprint,authority.UserId,authority.PersonalSessionId,now,b.ExpectedDraftVersion,next,all);
            db.AcceptedBatchReceipts.Add(new() { TaskId=taskId,BatchId=b.BatchId,RequestFingerprint=fingerprint,DraftVersion=next,ReceiptJson=Encode(outcome) });
            await db.SaveChangesAsync(ct);
            foreach(var r in newReceipts) db.ContributionOperations.Add(new() { TaskId=taskId,OperationId=r.OperationId,BatchId=b.BatchId,
                TaskItemId=r.TaskItemId,InspectionAttemptId=r.InspectionAttemptId,SampleId=r.SampleId,SourceWorkContextId=r.SourceWorkContextId,
                ContributorUserId=r.ContributorUserId,UploadedByUserId=r.UploadedByUserId,UploadedPersonalSessionId=r.UploadedPersonalSessionId,
                EnvironmentId=r.EnvironmentId,LineId=r.LineId,Kind=r.Kind,ContentHash=r.ContentHash,CanonicalPayload=payloads[r.OperationId],
                ReceiptJson=Encode(r),AcceptedAtUtc=now,DraftVersion=next });
            await db.SaveChangesAsync(ct);
            foreach(var op in ordered) foreach(var dep in op.Dependencies) db.ContributionDependencies.Add(new() {
                TaskId=taskId,OperationId=op.OperationId,DependencyId=dep.OperationId,ContentHash=dep.ContentHash.ToUpperInvariant() });
            foreach(var p in projections.Values.Where(x=>newReceipts.Any(r=>r.SampleId==x.SampleId))) {
                if(existingProjectionIds.Contains(p.SampleId)) {
                    db.SharedSampleProjections.Attach(p);
                    foreach(var name in new[]{nameof(p.LastOperationId),nameof(p.ValueOperationId),nameof(p.InspectionType),nameof(p.InspectionValue),nameof(p.Judgment),nameof(p.Barcode),nameof(p.IsRemoved)})
                        db.Entry(p).Property(name).IsModified=true;
                } else db.SharedSampleProjections.Add(p);
            }
            await db.SaveChangesAsync(ct);
            db.SharedAcceptanceAudits.Add(new() { Id=Guid.NewGuid(),TaskId=taskId,BatchId=b.BatchId,ActorUserId=authority.UserId,
                PersonalSessionId=authority.PersonalSessionId,OccurredAtUtc=now,BeforeVersion=b.ExpectedDraftVersion,AfterVersion=next,NewOperationCount=fresh.Count });
            return new DraftBatchResult(outcome,all,false);
        },ct);
    }

    private static Guid[] SourceIds(DraftBatchInput b)=>b.Operations?.Take(51).Where(x=>x is not null)
        .Select(x=>x.SourceWorkContextId).Distinct().ToArray() ?? [];

    private static void Shape(DraftBatchInput b)
    {
        bool ReferencesValid(List<DraftOperationReference>? rs)=>rs is {Count:<=100} && rs.All(r=>r is not null && r.OperationId!=Guid.Empty && DraftCanonical.IsHash(r.ContentHash)) && rs.Select(x=>x.OperationId).Distinct().Count()==rs.Count;
        if(b.BatchId==Guid.Empty || b.ExpectedDraftVersion<0 || b.ExpectedTaskLineId==Guid.Empty ||
            b.ExpectedEnvironmentId is not {Length:>0 and <=200} || b.Operations is not {Count:<=50} || !ReferencesValid(b.AcceptedReferences) ||
            b.SourceProofs is not {Count:<=50} || b.SourceProofs.Any(x=>x is null || x.WorkContextId==Guid.Empty) ||
            b.SourceProofs.Select(x=>x.WorkContextId).Distinct().Count()!=b.SourceProofs.Count || b.Operations.Any(o=>o is null ||
                o.OperationId==Guid.Empty || o.TaskItemId==Guid.Empty || o.InspectionAttemptId==Guid.Empty || o.ClientSampleId==Guid.Empty ||
                o.SourceWorkContextId==Guid.Empty || o.ContributorUserIdAssertion==Guid.Empty || o.BaseDraftVersion<0 || o.OriginLocalEditVersion<0 ||
                o.Kind is not {Length:>0 and <=40} || !ReferencesValid(o.Dependencies)) ||
            b.Operations.Select(x=>x.OperationId).Distinct().Count()!=b.Operations.Count ||
            b.Operations.Any(x=>b.AcceptedReferences.Any(r=>r.OperationId==x.OperationId)) || b.Operations.Count+b.AcceptedReferences.Count==0)
            throw Error("InvalidDraftBatch",400);
    }

    private static List<DraftOperationInput> Manifest(DraftBatchInput b,Dictionary<Guid,DraftOperationInput> fresh,
        Dictionary<Guid,ContributionOperation> known,Dictionary<Guid,string> hashes)
    {
        var ordered=new List<DraftOperationInput>(); var visiting=new HashSet<Guid>();var done=new HashSet<Guid>();
        void Visit(Guid id) {
            if(done.Contains(id)) return; if(!visiting.Add(id)) throw Error("CyclicDependency",400);
            foreach(var d in fresh[id].Dependencies.OrderBy(x=>DraftCanonical.Key(x.OperationId),StringComparer.Ordinal)) if(fresh.ContainsKey(d.OperationId)) Visit(d.OperationId);
            visiting.Remove(id);done.Add(id);ordered.Add(fresh[id]);
        }
        foreach(var id in fresh.Keys.OrderBy(DraftCanonical.Key,StringComparer.Ordinal)) Visit(id);
        var frontier=new HashSet<Guid>();
        foreach(var op in fresh.Values) foreach(var d in op.Dependencies) {
            if(hashes.TryGetValue(d.OperationId,out var h)) { if(h!=d.ContentHash.ToUpperInvariant()) throw Error("DependencyHashConflict"); }
            else if(b.AcceptedReferences.Any(x=>x.OperationId==d.OperationId) && known.TryGetValue(d.OperationId,out var old) && old.ContentHash==d.ContentHash.ToUpperInvariant()) frontier.Add(d.OperationId);
            else throw Error("MissingDependency");
        }
        if(!frontier.SetEquals(b.AcceptedReferences.Select(x=>x.OperationId))) throw Error("DependencyManifestConflict");
        return ordered;
    }

    private sealed record SourceClaim(Guid OperationId, Guid SourceWorkContextId, Guid ContributorUserIdAssertion, List<Guid> Dependencies);
    private Task<Dictionary<Guid,WorkContext>> Sources(PatrolTask t,DraftBatchInput b,List<DraftOperationInput> fresh,PersonalAuthority authority,CancellationToken ct)=>
        Sources(t,b.SourceProofs,fresh.Select(x=>new SourceClaim(x.OperationId,x.SourceWorkContextId,x.ContributorUserIdAssertion,x.Dependencies.Select(d=>d.OperationId).ToList())).ToList(),authority,ct);

    private async Task<Dictionary<Guid,WorkContext>> Sources(PatrolTask t,List<DraftSourceProof> proofs,List<SourceClaim> fresh,PersonalAuthority authority,CancellationToken ct)
    {
        var ids=fresh.Select(x=>x.SourceWorkContextId).ToHashSet();
        if(proofs.Any(x=>!ids.Contains(x.WorkContextId))) throw Error("SourceManifestConflict",400);
        var contexts=await db.WorkContexts.AsNoTracking().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var userIds=contexts.Values.Select(x=>x.UserId).Distinct().ToArray();
        var users=await db.ApplicationUsers.AsNoTracking().Where(x=>userIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var invalid=new HashSet<Guid>();var revoked=new HashSet<Guid>();
        foreach(var op in fresh) {
            var id=op.SourceWorkContextId;
            if(!contexts.TryGetValue(id,out var c) || !WorkContextService.ValidProof(proofs.SingleOrDefault(x=>x.WorkContextId==id)?.ProofSecret,c.ProofSecretHash) ||
                c.ContractVersion!=1 || c.WorkflowVersion!=2 || c.TaskId!=t.Id || c.LineId!=t.LineId || c.EnvironmentId!=authority.EnvironmentId ||
                c.UserId!=op.ContributorUserIdAssertion || c.IssuedPersonalSessionId==Guid.Empty || c.IssuedAtUtc>=c.CaptureUntilUtc ||
                c.CaptureUntilUtc>c.PersonalSessionExpiresAtUtc || c.CaptureUntilUtc-c.IssuedAtUtc>TimeSpan.FromHours(72))
                {invalid.Add(id);continue;}
            if(c.SecurityRevokedAtUtc is not null || !users.TryGetValue(c.UserId,out var owner) || !owner.IsActive || owner.Role!="IPQA") revoked.Add(id);
        }
        var bad=invalid.Concat(revoked).ToHashSet();
        if(bad.Count>0) {
            var affected=fresh.Where(x=>bad.Contains(x.SourceWorkContextId)).Select(x=>x.OperationId).ToHashSet();bool changed;
            do {changed=false;foreach(var op in fresh) if(op.Dependencies.Any(affected.Contains)) changed|=affected.Add(op.OperationId);} while(changed);
            throw new DraftQuarantineException(invalid.Count>0 ? "SourceProvenanceConflict" : "SourceSecurityRevoked",
                affected.Order().ToArray(),bad.Order().ToArray());
        }
        return contexts;
    }

    private static DraftSampleValue Value(PatrolTaskItem item,DraftSampleInput? data)
    {
        if(data is null || data.Barcode is {Length:>200} || data.Barcode?.Any(char.IsControl)==true) throw Error("InvalidSampleValue",400);
        var type=InspectionJudgmentService.CanonicalType(item.InspectionType);
        if(type=="Qualitative" && data.InspectionValue is null && data.Barcode is null && data.Judgment is "OK" or "NG") return new(type,null,data.Judgment,null);
        if(type=="Quantitative" && data.Judgment is null && data.InspectionValue is { } value && InspectionJudgmentService.QuantitativeSampleJudgment(item,value) is { } judgment)
            return new(type,value,judgment,data.Barcode);
        throw Error("InvalidSampleValue",400);
    }

    public Task<DraftBatchReceipt> BatchReceiptAsync(Guid taskId,Guid batchId,ClaimsPrincipal actor,CancellationToken ct)=>
        store.RunAsync(actor,false,taskId,[],async _=> { await identity.Target(taskId,null,ct);
            var b=await db.AcceptedBatchReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.TaskId==taskId && x.BatchId==batchId,ct) ?? throw Error("NotFound",404);
            return Decode<DraftBatchReceipt>(b.ReceiptJson);
        },ct);
    public Task<object> OperationReceiptAsync(Guid taskId,Guid operationId,ClaimsPrincipal actor,CancellationToken ct)=>
        store.RunAsync<object>(actor,false,taskId,[],async _=> { await identity.Target(taskId,null,ct);
            var o=await db.ContributionOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.TaskId==taskId && x.OperationId==operationId,ct);
            if(o is not null) return Receipt(o);
            var removal=await db.ItemRemovalOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.TaskId==taskId && x.OperationId==operationId,ct) ?? throw Error("NotFound",404);
            return Decode<ItemRemovalReceipt>(removal.ReceiptJson);
        },ct);
    public Task<SharedDraftOutput> ReadAsync(Guid taskId,ClaimsPrincipal actor,CancellationToken ct)=>
        store.RunAsync(actor,false,taskId,[],async _=> {
            var t=await identity.Target(taskId,null,ct);
            var removals=await db.ItemRemovalOperations.AsNoTracking().Where(x=>x.TaskId==taskId).ToDictionaryAsync(x=>x.InspectionAttemptId,ct);
            var samples=await db.SharedSamples.AsNoTracking().Where(x=>x.TaskId==taskId).OrderBy(x=>x.TaskItemId).ThenBy(x=>x.SequenceNo).ToListAsync(ct);
            var p=await db.SharedSampleProjections.AsNoTracking().Where(x=>x.TaskId==taskId).ToDictionaryAsync(x=>x.SampleId,ct);
            var opIds=p.Values.Select(x=>x.ValueOperationId).Distinct().ToArray();
            var values=await db.ContributionOperations.AsNoTracking().Where(x=>x.TaskId==taskId && opIds.Contains(x.OperationId)).ToDictionaryAsync(x=>x.OperationId,ct);
            var attempts=t.Items.Where(i=>i.CurrentInspectionAttempt is not null).Select(i=>new DraftAttemptScope(i.Id,
                i.CurrentInspectionAttempt!.InspectionAttemptId,i.InspectionAttempts.Single(a=>a.Id==i.CurrentInspectionAttempt.InspectionAttemptId).MinimumSampleCount)).ToList();
            return new SharedDraftOutput(t.Id,t.LineId!.Value,2,t.SharedDraft?.DraftVersion ?? 0,InspectionAttemptService.Category(t),attempts,samples.Select(s=> {
                var projection=p[s.Id];var source=values[projection.ValueOperationId];
                return new DraftSampleOutput(s.Id,s.TaskItemId,s.InspectionAttemptId,s.ClientSampleId,s.SequenceNo,
                    new(projection.InspectionType,projection.InspectionValue,projection.Judgment,projection.Barcode),projection.IsRemoved,
                    projection.ValueOperationId,source.ContributorUserId,projection.LastOperationId,Current(t,s.TaskItemId,s.InspectionAttemptId) && !removals.ContainsKey(s.InspectionAttemptId));
            }).ToList(),Items:attempts.Select(a=>new DraftItemOutput(a.TaskItemId,a.InspectionAttemptId,
                removals.ContainsKey(a.InspectionAttemptId)?"Removed":"Applicable",a.MinimumSampleCount,
                removals.TryGetValue(a.InspectionAttemptId,out var r)?Decode<ItemRemovalReceipt>(r.ReceiptJson):null)).ToList(),
                AllItemsRemoved:t.Items.Count>0 && attempts.Count==t.Items.Count && attempts.All(a=>removals.ContainsKey(a.InspectionAttemptId)));
        },ct);
}
