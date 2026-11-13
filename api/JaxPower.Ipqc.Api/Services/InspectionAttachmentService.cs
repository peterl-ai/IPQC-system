using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class InspectionAttachmentService(IpqcDbContext db,WorkContextStore store,InspectionAttemptService identity,
    SharedDraftService sources,IProtectedPhotoStore files,PhotoVerifier verifier,TimeProvider clock)
{
    private static PersonalAccessException Error(string code,int status=409)=>new(code,status);
    private DateTime Now()=>LineSharedPlanService.Micro(clock.GetUtcNow().UtcDateTime);
    private static DateTime Utc(DateTime date)=>DateTime.SpecifyKind(date,DateTimeKind.Utc);
    private async Task<PatrolTask> Editable(InspectionAttachment a,CancellationToken ct)
    {
        var t=await identity.Target(a.TaskId,a.LineId,ct);
        if(PlanningSnapshot.Status(t)!="ValidV1" || InitialInspectionIdentity.State(t)!="Initialized" ||
            LineSharedTaskEligibility.Availability(t) is not ("FoundationOnly" or "DraftFoundation") ||
            !t.Items.Any(i=>i.Id==a.TaskItemId && i.CurrentInspectionAttempt?.InspectionAttemptId==a.InspectionAttemptId) ||
            await db.ItemRemovalOperations.AnyAsync(x=>x.TaskId==a.TaskId && x.InspectionAttemptId==a.InspectionAttemptId,ct)) throw Error("PhotoAttemptUnavailable");
        return t;
    }
    private async Task ValidateSource(InspectionAttachment a,string proof,PersonalAuthority b,CancellationToken ct)
    {
        if(a.EnvironmentId!=b.EnvironmentId) throw Error("PhotoScopeMismatch");
        await sources.ValidatePhotoSource(await Editable(a,ct),a.ClientPhotoId,a.SourceWorkContextId,a.ContributorUserId,proof,b,ct);
    }
    private void Event(AttachmentAvailability v,string state,PersonalAuthority b)
    {
        v.State=state;v.Version=checked(v.Version+1);
        db.AttachmentEvents.Add(new(){Id=Guid.NewGuid(),AttachmentId=v.AttachmentId,State=state,ActorUserId=b.UserId,PersonalSessionId=b.PersonalSessionId,OccurredAtUtc=Now()});
    }
    private async Task<AttachmentAvailability> Availability(Guid id,CancellationToken ct)
    {
        if(db.Database.IsNpgsql())
            _=await db.AttachmentAvailabilities.FromSqlInterpolated($"SELECT * FROM \"AttachmentAvailabilities\" WHERE \"AttachmentId\" = {id} FOR UPDATE").AsNoTracking().ToListAsync(ct);
        return await db.AttachmentAvailabilities.SingleAsync(x=>x.AttachmentId==id,ct);
    }
    private static bool Same(InspectionAttachment a,Guid task,AttachmentInput i)=>a.TaskId==task && a.TaskItemId==i.TaskItemId && a.InspectionAttemptId==i.InspectionAttemptId &&
        a.LineId==i.ExpectedTaskLineId && a.WorkflowVersion==i.ExpectedWorkflowVersion && a.EnvironmentId==i.ExpectedEnvironmentId && a.SourceWorkContextId==i.SourceWorkContextId &&
        a.ContributorUserId==i.ContributorUserIdAssertion && a.ExpectedBytes==i.ByteCount && a.ExpectedSha256==i.Sha256.ToUpperInvariant() && a.DeclaredContentType==i.ContentType && a.CaptureSource==i.CaptureSource && i.ContractVersion==1;

    public async Task<AttachmentOutput> Reserve(Guid taskId,AttachmentInput input,ClaimsPrincipal actor,CancellationToken ct)
    {
        Guid id;
        try { id=await store.RunAsync(actor,false,taskId,[input.SourceWorkContextId],async b=> {
            await identity.Target(taskId,input.ExpectedTaskLineId,ct);
            if(input.ClientPhotoId==Guid.Empty || input.TaskItemId==Guid.Empty || input.InspectionAttemptId==Guid.Empty || input.SourceWorkContextId==Guid.Empty ||
                input.ContributorUserIdAssertion==Guid.Empty || input.ContractVersion!=1 || input.ExpectedWorkflowVersion!=2 || input.ExpectedEnvironmentId!=b.EnvironmentId ||
                input.ByteCount is <1 or >ProtectedPhotoStore.MaximumBytes || !DraftCanonical.IsHash(input.Sha256) || input.ContentType is not ("image/jpeg" or "image/png") ||
                input.CaptureSource is not ("Camera" or "Album")) throw Error("InvalidPhotoManifest",400);
            var old=await db.InspectionAttachments.SingleOrDefaultAsync(x=>x.EnvironmentId==b.EnvironmentId && x.ClientPhotoId==input.ClientPhotoId,ct);
            if(old is not null) { if(!Same(old,taskId,input)) throw Error("ClientPhotoIdentityConflict");return old.Id; }
            files.CheckConfigured();
            var a=new InspectionAttachment {Id=Guid.NewGuid(),ClientPhotoId=input.ClientPhotoId,TaskId=taskId,TaskItemId=input.TaskItemId,InspectionAttemptId=input.InspectionAttemptId,
                LineId=input.ExpectedTaskLineId,EnvironmentId=b.EnvironmentId,SourceWorkContextId=input.SourceWorkContextId,ContributorUserId=input.ContributorUserIdAssertion,
                ExpectedBytes=input.ByteCount,ExpectedSha256=input.Sha256.ToUpperInvariant(),DeclaredContentType=input.ContentType,CaptureSource=input.CaptureSource,StorageKey=Guid.NewGuid().ToString("N"),CreatedAtUtc=Now()};
            await ValidateSource(a,input.ProofSecret,b,ct);
            if(await db.InspectionAttachments.CountAsync(x=>x.InspectionAttemptId==a.InspectionAttemptId,ct)>=5) throw Error("PhotoSlotLimitReached");
            db.InspectionAttachments.Add(a);await db.SaveChangesAsync(ct);
            var v=new AttachmentAvailability {AttachmentId=a.Id};db.AttachmentAvailabilities.Add(v);Event(v,"AwaitingBytes",b);return a.Id;
        },ct); }
        catch(DbUpdateException ex) when(ex.InnerException is Npgsql.PostgresException {SqlState:"23505",ConstraintName:"IX_InspectionAttachments_EnvironmentId_ClientPhotoId"}) {
            // Different task/account lock sets can race on the environment-wide client ID.
            // The losing transaction has rolled back; resolve the winner under fresh authority.
            id=await store.RunAsync(actor,false,taskId,[],async b=> {
                await identity.Target(taskId,input.ExpectedTaskLineId,ct);
                var winner=await db.InspectionAttachments.AsNoTracking().SingleAsync(x=>x.EnvironmentId==b.EnvironmentId && x.ClientPhotoId==input.ClientPhotoId,ct);
                if(!Same(winner,taskId,input)) throw Error("ClientPhotoIdentityConflict");return winner.Id;
            },ct);
        }
        return await Read(taskId,id,false,actor,ct);
    }
    private async Task<InspectionAttachment> Find(Guid taskId,Guid id,bool client,CancellationToken ct) =>
        await db.InspectionAttachments.AsNoTracking().SingleOrDefaultAsync(x=>x.TaskId==taskId && (client?x.ClientPhotoId==id:x.Id==id),ct) ?? throw Error("NotFound",404);

    private async Task<T> Run<T>(Guid taskId,Guid id,ClaimsPrincipal actor,bool source,Func<InspectionAttachment,AttachmentAvailability,PersonalAuthority,Task<T>> action,CancellationToken ct)
    {
        // Only immutable discovery; authority and resource are re-read after account-first locks.
        var sourceId=await db.InspectionAttachments.AsNoTracking().Where(x=>x.TaskId==taskId && x.Id==id).Select(x=>(Guid?)x.SourceWorkContextId).SingleOrDefaultAsync(ct);
        return await store.RunAsync(actor,false,taskId,source && sourceId is {} s?[s]:[],async b=> {
            await identity.Target(taskId,null,ct);var a=await Find(taskId,id,false,ct);
            if(a.EnvironmentId!=b.EnvironmentId) throw Error("NotFound",404);
            return await action(a,await Availability(id,ct),b);
        },ct);
    }
    private sealed record UploadLease(InspectionAttachment Attachment,Guid UploadId,string StageKey,Guid Token,bool Accepted);
    private async Task<UploadLease> Begin(Guid taskId,Guid id,string proof,bool recover,ClaimsPrincipal actor,CancellationToken ct) =>
        await Run(taskId,id,actor,true,async(a,v,b)=> {
            if(v.AcceptedAttachmentId is not null) return new UploadLease(a,Guid.Empty,"",Guid.Empty,true);
            await ValidateSource(a,proof,b,ct);files.CheckConfigured();
            if(v.LeaseUntilUtc is {} until && until>Now()) throw Error("PhotoUploadInProgress");
            AttachmentUpload? u=null;
            if(recover && v.ActiveUploadId is {} old) u=await db.AttachmentUploads.SingleAsync(x=>x.Id==old && x.AttachmentId==id,ct);
            if(recover && u is null) throw Error("NoPhotoUploadToRecover");
            if(!recover) {
                if(await db.AttachmentUploads.CountAsync(x=>x.AttachmentId==id,ct)>=10) throw Error("PhotoUploadAttemptLimitReached");
                u=new(){Id=Guid.NewGuid(),AttachmentId=id,StageKey=Guid.NewGuid().ToString("N"),StartedByUserId=b.UserId,StartedPersonalSessionId=b.PersonalSessionId,StartedAtUtc=Now()};
                db.AttachmentUploads.Add(u);await db.SaveChangesAsync(ct);
            }
            v.ActiveUploadId=u!.Id;v.LeaseToken=Guid.NewGuid();v.LeaseUntilUtc=Now().AddMinutes(10);Event(v,"Receiving",b);
            return new UploadLease(a,u.Id,u.StageKey,v.LeaseToken.Value,false);
        },ct);
    private void Fence(AttachmentAvailability v,UploadLease lease)
    {
        if(v.ActiveUploadId!=lease.UploadId || v.LeaseToken!=lease.Token || v.AcceptedAttachmentId is not null || v.LeaseUntilUtc is null || v.LeaseUntilUtc<=Now()) throw Error("PhotoUploadLeaseLost");
    }
    public async Task<AttachmentOutput> Upload(Guid taskId,Guid id,string proof,Stream body,ClaimsPrincipal actor,CancellationToken ct)
    {
        var lease=await Begin(taskId,id,proof,false,actor,ct);var a=lease.Attachment;
        if(lease.Accepted) {
            // Exact repeat still checks actual supplied bytes, without overwriting or revalidating A.
            using var bytes=new MemoryStream();var chunk=new byte[65536];int count;
            while((count=await body.ReadAsync(chunk,ct))!=0) {if(bytes.Length+count>a.ExpectedBytes) throw Error("ClientPhotoIdentityConflict");bytes.Write(chunk,0,count);}
            try {await verifier.Verify(bytes.ToArray(),a.ExpectedBytes,a.ExpectedSha256,a.DeclaredContentType,ct);}
            catch(PhotoStorageException) {throw Error("ClientPhotoIdentityConflict");}
            return await ReadyResult(taskId,id,actor,ct);
        }
        try {await files.Stage(lease.StageKey,body,a.ExpectedBytes,ct);return await Finish(taskId,id,proof,lease,actor,ct);}
        catch {await Failed(taskId,id,lease,actor);throw;}
    }
    public async Task<AttachmentOutput> Recover(Guid taskId,Guid id,string proof,ClaimsPrincipal actor,CancellationToken ct)
    {
        var lease=await Begin(taskId,id,proof,true,actor,ct);
        if(lease.Accepted) return await ReadyResult(taskId,id,actor,ct);
        try {return await Finish(taskId,id,proof,lease,actor,ct);}
        catch {await Failed(taskId,id,lease,actor);throw;}
    }
    private async Task<AttachmentOutput> Finish(Guid taskId,Guid id,string proof,UploadLease lease,ClaimsPrincipal actor,CancellationToken ct)
    {
        var a=lease.Attachment;var bytes=await files.Read(a.StorageKey,true,ct);var final=bytes is not null;
        bytes??=await files.Read(lease.StageKey,false,ct) ?? throw new PhotoStorageException("PhotoBytesMissing");
        var verified=await verifier.Verify(bytes,a.ExpectedBytes,a.ExpectedSha256,a.DeclaredContentType,ct);
        // Recovery flushes an existing stage again: the original stream may have died before flush.
        // Stage() has already flushed normal uploads; recovery must establish the same guarantee.
        if(files is IPhotoDurability durability) durability.FlushExisting(final?a.StorageKey:lease.StageKey,final);
        else throw new PhotoStorageException("PhotoDurabilityUnavailable");
        var stored=await Run(taskId,id,actor,true,async(locked,v,b)=> {
            Fence(v,lease);await ValidateSource(locked,proof,b,ct);
            var u=await db.AttachmentUploads.SingleAsync(x=>x.Id==lease.UploadId,ct);u.StoredAtUtc??=Now();Event(v,"Stored",b);return u.StoredAtUtc.Value;
        },ct);
        if(!final) files.Promote(lease.StageKey,a.StorageKey);
        var promoted=await files.Read(a.StorageKey,true,ct) ?? throw new PhotoStorageException("PhotoBytesMissing");
        await verifier.Verify(promoted,a.ExpectedBytes,a.ExpectedSha256,a.DeclaredContentType,ct);
        await Run(taskId,id,actor,true,async(locked,v,b)=> {
            Fence(v,lease);await ValidateSource(locked,proof,b,ct);
            db.AttachmentAcceptances.Add(new(){AttachmentId=id,UploadId=lease.UploadId,UploadedByUserId=b.UserId,UploadedPersonalSessionId=b.PersonalSessionId,
                ByteCount=verified.ByteCount,Sha256=verified.Sha256,ContentType=verified.ContentType,StoredEncoding=verified.Encoding,Width=verified.Width,Height=verified.Height,StoredAtUtc=stored,ReadyAtUtc=Now()});
            await db.SaveChangesAsync(ct);v.AcceptedAttachmentId=id;v.LeaseToken=null;v.LeaseUntilUtc=null;Event(v,"Ready",b);return true;
        },ct);
        return await ReadyResult(taskId,id,actor,ct);
    }
    private async Task<AttachmentOutput> ReadyResult(Guid taskId,Guid id,ClaimsPrincipal actor,CancellationToken ct)
    {
        var output=await Read(taskId,id,false,actor,ct);
        if(!output.IsReady) throw Error("PhotoNotAvailable");
        return output;
    }
    private async Task Failed(Guid taskId,Guid id,UploadLease lease,ClaimsPrincipal actor)
    {
        try {await Run(taskId,id,actor,false,(a,v,b)=> {
            if(v.LeaseToken==lease.Token && v.AcceptedAttachmentId is null) {v.LeaseToken=null;v.LeaseUntilUtc=null;Event(v,"Failed",b);}return Task.FromResult(true);
        },CancellationToken.None);} catch { /* Durable Receiving/Stored + expiring lease remains recoverable if DB/B unavailable. */ }
    }
    public async Task<AttachmentOutput> Read(Guid taskId,Guid id,bool client,ClaimsPrincipal actor,CancellationToken ct)
    {
        var a=await store.RunAsync(actor,false,taskId,[],async b=> {
            await identity.Target(taskId,null,ct);var found=await Find(taskId,id,client,ct);if(found.EnvironmentId!=b.EnvironmentId) throw Error("NotFound",404);return found;
        },ct);
        await Probe(taskId,a,actor,ct);
        return await Run(taskId,a.Id,actor,false,async(locked,v,b)=>Output(locked,v,await db.AttachmentAcceptances.AsNoTracking().SingleOrDefaultAsync(x=>x.AttachmentId==a.Id,ct)),ct);
    }
    private async Task<byte[]?> Probe(Guid taskId,InspectionAttachment a,ClaimsPrincipal actor,CancellationToken ct)
    {
        var accepted=await db.AttachmentAcceptances.AsNoTracking().AnyAsync(x=>x.AttachmentId==a.Id,ct);if(!accepted) return null;
        string? failure=null;byte[]? bytes=null;
        try {bytes=await files.Read(a.StorageKey,true,ct);if(bytes is null) failure="MissingUnexpected";
            else await verifier.Verify(bytes,a.ExpectedBytes,a.ExpectedSha256,a.DeclaredContentType,ct);}
        catch(PhotoStorageException ex) when(ex.Message is "PhotoContentMismatch" or "PhotoBytesCorrupt" or "UnsupportedOrCorruptPhoto" or "PhotoTypeMismatch") {failure="Corrupt";}
        if(failure is not null) await Run(taskId,a.Id,actor,false,(locked,v,b)=>{if(v.State!=failure) Event(v,failure,b);return Task.FromResult(true);},ct);
        return failure is null?bytes:null;
    }
    public async Task<(byte[] Bytes,string ContentType)> Content(Guid taskId,Guid id,ClaimsPrincipal actor,CancellationToken ct)
    {
        var output=await Read(taskId,id,false,actor,ct);
        if(!output.IsReady) throw Error("PhotoNotAvailable");
        var a=await Find(taskId,id,false,ct);var bytes=await Probe(taskId,a,actor,ct) ?? throw Error("PhotoNotAvailable");
        return (bytes,output.ContentType!);
    }
    private static AttachmentOutput Output(InspectionAttachment a,AttachmentAvailability v,AttachmentAcceptance? c)=>new(a.Id,a.ClientPhotoId,a.TaskId,a.TaskItemId,a.InspectionAttemptId,
        a.LineId,a.WorkflowVersion,a.EnvironmentId,a.SourceWorkContextId,a.ContributorUserId,a.CaptureSource,Utc(a.CreatedAtUtc),v.State,v.State=="Ready" && c is not null,
        c?.UploadedByUserId,c?.UploadedPersonalSessionId,c?.ByteCount,c?.Sha256,c?.ContentType,c?.StoredEncoding,c?.Width,c?.Height,c is null?null:Utc(c.StoredAtUtc),c is null?null:Utc(c.ReadyAtUtc));
}
