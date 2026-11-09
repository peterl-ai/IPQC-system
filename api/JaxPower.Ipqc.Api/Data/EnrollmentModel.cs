using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Data;

public sealed partial class IpqcDbContext
{
    public DbSet<DeviceEnrollment> DeviceEnrollments => Set<DeviceEnrollment>();
    public DbSet<EnrollmentChallenge> EnrollmentChallenges => Set<EnrollmentChallenge>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    private static void ConfigureEnrollment(ModelBuilder model)
    {
        var e = model.Entity<DeviceEnrollment>(); e.HasKey(x => x.Id);
        e.Property(x => x.Revision).IsConcurrencyToken();
        e.Property(x => x.Environment).HasMaxLength(200).IsRequired();
        foreach (var name in new[] { nameof(DeviceEnrollment.InvitationHash), nameof(DeviceEnrollment.CsrSha256), nameof(DeviceEnrollment.SpkiSha256), nameof(DeviceEnrollment.ApprovalContentHash) }) e.Property<string>(name).HasMaxLength(64);
        e.Property(x => x.TerminalReason).HasMaxLength(2000);
        e.HasIndex(x => x.DeviceId).HasDatabaseName("UX_Enrollment_OpenDevice").IsUnique().HasFilter("\"State\" IN (1, 2, 3, 4, 5, 6)");
        e.HasIndex(x => x.ApprovalOperationId).HasDatabaseName("UX_Enrollment_ApprovalOperation").IsUnique();
        e.HasOne<RegisteredDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).HasConstraintName("FK_Enrollment_Creator").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).HasConstraintName("FK_Enrollment_Approver").OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_Enrollment_State", "\"State\" BETWEEN 1 AND 8 AND \"Revision\" >= 1 AND \"SourceBindingVersion\" >= 1 AND \"TargetBindingVersion\" = \"SourceBindingVersion\" + 1 AND length(\"InvitationHash\") = 64 AND length(trim(\"Environment\")) > 0 AND \"CreatedAtUtc\" < \"InvitationExpiresAtUtc\" AND \"InvitationExpiresAtUtc\" <= \"ExpiresAtUtc\" AND (\"State\" NOT IN (3,4,5,6) OR (\"InvitationConsumedAtUtc\" IS NOT NULL AND \"ProofVerifiedAtUtc\" IS NOT NULL AND \"SpkiBase64\" IS NOT NULL AND \"CsrDerBase64\" IS NOT NULL)) AND (\"State\" NOT IN (4,5,6) OR (\"ApprovedByUserId\" IS NOT NULL AND \"ApprovalOperationId\" IS NOT NULL AND \"ApprovedAtUtc\" IS NOT NULL))"));

        var c = model.Entity<EnrollmentChallenge>(); c.HasKey(x => x.Id); c.Property(x => x.Revision).IsConcurrencyToken();
        c.Property(x => x.NonceHash).HasMaxLength(64); c.Property(x => x.PayloadHash).HasMaxLength(64);
        c.HasOne<DeviceEnrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        c.HasIndex(x => new { x.EnrollmentId, x.Purpose, x.ExpiresAtUtc }).HasDatabaseName("IX_Challenge_RequestPurposeExpiry");
        c.ToTable(t => t.HasCheckConstraint("CK_Challenge_State", "\"Purpose\" IN (1,2) AND \"Revision\" >= 1 AND length(\"NonceHash\") = 64 AND length(\"PayloadHash\") = 64"));

        var k = model.Entity<DeviceCredential>(); k.HasKey(x => x.Id); k.Property(x => x.Revision).IsConcurrencyToken();
        k.Property(x => x.IssuerId).HasMaxLength(64); k.Property(x => x.CertificateSha256).HasMaxLength(64); k.Property(x => x.LastErrorCode).HasMaxLength(80);
        k.HasIndex(x => x.EnrollmentId).IsUnique(); k.HasIndex(x => x.IssuanceOperationId).IsUnique(); k.HasIndex(x => x.CertificateSha256).IsUnique();
        k.HasIndex(x => new { x.State, x.LeaseUntilUtc });
        k.HasOne<DeviceEnrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        k.HasOne<RegisteredDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        k.HasOne<ProductionLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        k.ToTable(t => t.HasCheckConstraint("CK_Candidate_State", "\"State\" BETWEEN 1 AND 5 AND \"Revision\" >= 1 AND \"BindingVersion\" >= 2 AND \"NotBeforeUtc\" < \"NotAfterUtc\" AND (\"State\" <> 3 OR (\"CertificatePem\" IS NOT NULL AND \"CertificateSha256\" IS NOT NULL AND \"IssuedAtUtc\" IS NOT NULL))"));

        var a = model.Entity<ProvisioningAuditEvent>();
        a.Property(x => x.ActorKind).HasDefaultValue(ProvisioningActorKind.Admin).HasSentinel(ProvisioningActorKind.Admin);
        a.HasOne<RegisteredDevice>().WithMany().HasForeignKey(x => x.ActorDeviceId).HasConstraintName("FK_ProvisioningAudit_ActorDevice").OnDelete(DeleteBehavior.Restrict);
        a.HasOne<DeviceEnrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).HasConstraintName("FK_ProvisioningAudit_Enrollment").OnDelete(DeleteBehavior.Restrict);
        a.HasOne<DeviceCredential>().WithMany().HasForeignKey(x => x.CredentialId).HasConstraintName("FK_ProvisioningAudit_Credential").OnDelete(DeleteBehavior.Restrict);
        a.ToTable(t => t.HasCheckConstraint("CK_ProvisioningAudit_Actor", "(\"ActorKind\" = 1 AND \"ActorUserId\" IS NOT NULL AND \"ActorDeviceId\" IS NULL) OR (\"ActorKind\" IN (2,3) AND \"ActorUserId\" IS NULL AND \"ActorDeviceId\" IS NOT NULL) OR (\"ActorKind\" = 4 AND \"ActorUserId\" IS NULL AND \"ActorDeviceId\" IS NULL)"));
    }
}
