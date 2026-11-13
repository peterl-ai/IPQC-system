using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<InspectionAttachment> InspectionAttachments => Set<InspectionAttachment>();
    public DbSet<AttachmentAvailability> AttachmentAvailabilities => Set<AttachmentAvailability>();
    public DbSet<AttachmentUpload> AttachmentUploads => Set<AttachmentUpload>();
    public DbSet<AttachmentAcceptance> AttachmentAcceptances => Set<AttachmentAcceptance>();
    public DbSet<AttachmentEvent> AttachmentEvents => Set<AttachmentEvent>();

    private static void ConfigureAttachments(ModelBuilder b)
    {
        var a=b.Entity<InspectionAttachment>(); a.HasKey(x=>x.Id);
        a.HasIndex(x=>new{x.EnvironmentId,x.ClientPhotoId}).IsUnique(); a.HasIndex(x=>x.StorageKey).IsUnique();
        a.Property(x=>x.EnvironmentId).HasMaxLength(200); a.Property(x=>x.StorageKey).HasMaxLength(32);
        a.Property(x=>x.ExpectedSha256).HasMaxLength(64); a.Property(x=>x.DeclaredContentType).HasMaxLength(20); a.Property(x=>x.CaptureSource).HasMaxLength(10);
        a.HasOne<PatrolTask>().WithMany().HasForeignKey(x=>x.TaskId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<PatrolTaskItem>().WithMany().HasForeignKey(x=>x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<InspectionAttempt>().WithMany().HasForeignKey(x=>new{x.InspectionAttemptId,x.TaskItemId}).HasPrincipalKey(x=>new{x.Id,x.PatrolTaskItemId}).HasConstraintName("FK_Photo_AttemptScope").OnDelete(DeleteBehavior.Restrict);
        a.HasOne<WorkContext>().WithMany().HasForeignKey(x=>x.SourceWorkContextId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ContributorUserId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ProductionLine>().WithMany().HasForeignKey(x=>x.LineId).OnDelete(DeleteBehavior.Restrict);
        a.ToTable(t=>t.HasCheckConstraint("CK_Attachment_Identity","\"WorkflowVersion\" = 2 AND \"ExpectedBytes\" BETWEEN 1 AND 5242880 AND length(\"ExpectedSha256\") = 64 AND length(\"StorageKey\") = 32 AND length(\"EnvironmentId\") BETWEEN 1 AND 200 AND \"DeclaredContentType\" IN ('image/jpeg','image/png') AND \"CaptureSource\" IN ('Camera','Album') AND \"ClientPhotoId\" <> '00000000-0000-0000-0000-000000000000'"));
        var u=b.Entity<AttachmentUpload>(); u.HasKey(x=>x.Id);u.HasAlternateKey(x=>new{x.Id,x.AttachmentId});
        u.HasIndex(x=>x.StageKey).IsUnique();u.Property(x=>x.StageKey).HasMaxLength(32);
        u.HasOne<InspectionAttachment>().WithMany().HasForeignKey(x=>x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        u.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
        u.ToTable(t=>t.HasCheckConstraint("CK_AttachmentUpload_Stored","length(\"StageKey\") = 32 AND (\"StoredAtUtc\" IS NULL OR \"StoredAtUtc\" >= \"StartedAtUtc\")"));
        var c=b.Entity<AttachmentAcceptance>();c.HasKey(x=>x.AttachmentId);
        c.Property(x=>x.Sha256).HasMaxLength(64);c.Property(x=>x.ContentType).HasMaxLength(20);c.Property(x=>x.StoredEncoding).HasMaxLength(10);
        c.HasOne<InspectionAttachment>().WithMany().HasForeignKey(x=>x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<AttachmentUpload>().WithMany().HasForeignKey(x=>new{x.UploadId,x.AttachmentId}).HasPrincipalKey(x=>new{x.Id,x.AttachmentId}).HasConstraintName("FK_PhotoAcceptance_UploadScope").OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        c.ToTable(t=>t.HasCheckConstraint("CK_AttachmentAcceptance_Ready","\"ByteCount\" BETWEEN 1 AND 5242880 AND length(\"Sha256\") = 64 AND \"Width\" BETWEEN 1 AND 6000 AND \"Height\" BETWEEN 1 AND 6000 AND CAST(\"Width\" AS bigint) * \"Height\" <= 12000000 AND \"ReadyAtUtc\" >= \"StoredAtUtc\" AND ((\"ContentType\" = 'image/jpeg' AND \"StoredEncoding\" = 'JPEG') OR (\"ContentType\" = 'image/png' AND \"StoredEncoding\" = 'PNG'))"));
        var v=b.Entity<AttachmentAvailability>();v.HasKey(x=>x.AttachmentId);v.Property(x=>x.State).HasMaxLength(30);v.Property(x=>x.Version).IsConcurrencyToken();
        v.HasOne<InspectionAttachment>().WithOne().HasForeignKey<AttachmentAvailability>(x=>x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<AttachmentUpload>().WithMany().HasForeignKey(x=>new{x.ActiveUploadId,x.AttachmentId}).HasPrincipalKey(x=>new{x.Id,x.AttachmentId}).HasConstraintName("FK_PhotoAvailability_UploadScope").OnDelete(DeleteBehavior.Restrict);
        v.HasOne<AttachmentAcceptance>().WithMany().HasForeignKey(x=>x.AcceptedAttachmentId).HasConstraintName("FK_PhotoAvailability_Acceptance").OnDelete(DeleteBehavior.Restrict);
        v.ToTable(t=>t.HasCheckConstraint("CK_AttachmentAvailability_State","\"Version\" >= 0 AND \"State\" IN ('AwaitingBytes','Receiving','Stored','Ready','Failed','MissingUnexpected','Corrupt') AND (\"State\" <> 'Ready' OR \"AcceptedAttachmentId\" IS NOT NULL) AND (\"AcceptedAttachmentId\" IS NULL OR \"AcceptedAttachmentId\" = \"AttachmentId\") AND ((\"LeaseToken\" IS NULL AND \"LeaseUntilUtc\" IS NULL) OR (\"LeaseToken\" IS NOT NULL AND \"LeaseUntilUtc\" IS NOT NULL AND \"ActiveUploadId\" IS NOT NULL))"));
        var e=b.Entity<AttachmentEvent>();e.HasKey(x=>x.Id);e.Property(x=>x.State).HasMaxLength(30);
        e.HasOne<InspectionAttachment>().WithMany().HasForeignKey(x=>x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
    private void GuardAttachments()
    {
        foreach(var e in ChangeTracker.Entries()) {
            if(e.Entity is InspectionAttachment or AttachmentAcceptance or AttachmentEvent && e.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Attachment identity and accepted history are immutable.");
            if(e.Entity is AttachmentUpload && (e.State==EntityState.Deleted || e.State==EntityState.Modified && e.Properties.Any(p=>p.IsModified && (p.Metadata.Name!=nameof(AttachmentUpload.StoredAtUtc) || p.OriginalValue is not null))))
                throw new InvalidOperationException("Upload identity and stored timestamp are immutable.");
        }
    }
}
