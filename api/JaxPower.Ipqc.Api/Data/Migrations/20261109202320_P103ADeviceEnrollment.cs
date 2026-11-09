using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class P103ADeviceEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guid = postgres ? "uuid" : "TEXT";
            var integer = postgres ? "integer" : "INTEGER";
            var big = postgres ? "bigint" : "INTEGER";
            var date = postgres ? "timestamp with time zone" : "TEXT";
            var boolean = postgres ? "boolean" : "INTEGER";
            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "ProvisioningAuditEvents",
                type: guid,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: guid);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorDeviceId",
                table: "ProvisioningAuditEvents",
                type: guid,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActorKind",
                table: "ProvisioningAuditEvents",
                type: integer,
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "CredentialId",
                table: "ProvisioningAuditEvents",
                type: guid,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EnrollmentId",
                table: "ProvisioningAuditEvents",
                type: guid,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    DeviceId = table.Column<Guid>(type: guid, nullable: false),
                    LineId = table.Column<Guid>(type: guid, nullable: false),
                    SourceBindingVersion = table.Column<long>(type: big, nullable: false),
                    TargetBindingVersion = table.Column<long>(type: big, nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<long>(type: big, nullable: false),
                    State = table.Column<int>(type: integer, nullable: false),
                    InvitationHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    InvitationExpiresAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    InvitationConsumedAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: guid, nullable: false),
                    CsrDerBase64 = table.Column<string>(type: "TEXT", nullable: true),
                    CsrSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SpkiBase64 = table.Column<string>(type: "TEXT", nullable: true),
                    SpkiSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ProofVerifiedAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: guid, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    ApprovalOperationId = table.Column<Guid>(type: guid, nullable: true),
                    ApprovalContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TerminalReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceEnrollments", x => x.Id);
                    table.CheckConstraint("CK_Enrollment_State", "\"State\" BETWEEN 1 AND 8 AND \"Revision\" >= 1 AND \"SourceBindingVersion\" >= 1 AND \"TargetBindingVersion\" = \"SourceBindingVersion\" + 1 AND length(\"InvitationHash\") = 64 AND length(trim(\"Environment\")) > 0 AND \"CreatedAtUtc\" < \"InvitationExpiresAtUtc\" AND \"InvitationExpiresAtUtc\" <= \"ExpiresAtUtc\" AND (\"State\" NOT IN (3,4,5,6) OR (\"InvitationConsumedAtUtc\" IS NOT NULL AND \"ProofVerifiedAtUtc\" IS NOT NULL AND \"SpkiBase64\" IS NOT NULL AND \"CsrDerBase64\" IS NOT NULL)) AND (\"State\" NOT IN (4,5,6) OR (\"ApprovedByUserId\" IS NOT NULL AND \"ApprovalOperationId\" IS NOT NULL AND \"ApprovedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_DeviceEnrollments_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceEnrollments_RegisteredDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "RegisteredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Enrollment_Approver",
                        column: x => x.ApprovedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Enrollment_Creator",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    EnrollmentId = table.Column<Guid>(type: guid, nullable: false),
                    DeviceId = table.Column<Guid>(type: guid, nullable: false),
                    LineId = table.Column<Guid>(type: guid, nullable: false),
                    BindingVersion = table.Column<long>(type: big, nullable: false),
                    IssuanceOperationId = table.Column<Guid>(type: guid, nullable: false),
                    State = table.Column<int>(type: integer, nullable: false),
                    Revision = table.Column<long>(type: big, nullable: false),
                    IssuerId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RootCertificatePem = table.Column<string>(type: "TEXT", nullable: false),
                    SpkiBase64 = table.Column<string>(type: "TEXT", nullable: false),
                    NotBeforeUtc = table.Column<DateTime>(type: date, nullable: false),
                    NotAfterUtc = table.Column<DateTime>(type: date, nullable: false),
                    CertificatePem = table.Column<string>(type: "TEXT", nullable: true),
                    CertificateSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    LeaseId = table.Column<Guid>(type: guid, nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: date, nullable: true),
                    ExternalRevocationPending = table.Column<bool>(type: boolean, nullable: false),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCredentials", x => x.Id);
                    table.CheckConstraint("CK_Candidate_State", "\"State\" BETWEEN 1 AND 5 AND \"Revision\" >= 1 AND \"BindingVersion\" >= 2 AND \"NotBeforeUtc\" < \"NotAfterUtc\" AND (\"State\" <> 3 OR (\"CertificatePem\" IS NOT NULL AND \"CertificateSha256\" IS NOT NULL AND \"IssuedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_DeviceCredentials_DeviceEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "DeviceEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceCredentials_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceCredentials_RegisteredDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "RegisteredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    EnrollmentId = table.Column<Guid>(type: guid, nullable: false),
                    Purpose = table.Column<int>(type: integer, nullable: false),
                    NonceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    Revision = table.Column<long>(type: big, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentChallenges", x => x.Id);
                    table.CheckConstraint("CK_Challenge_State", "\"Purpose\" IN (1,2) AND \"Revision\" >= 1 AND length(\"NonceHash\") = 64 AND length(\"PayloadHash\") = 64");
                    table.ForeignKey(
                        name: "FK_EnrollmentChallenges_DeviceEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "DeviceEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_ActorDeviceId",
                table: "ProvisioningAuditEvents",
                column: "ActorDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_CredentialId",
                table: "ProvisioningAuditEvents",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_EnrollmentId",
                table: "ProvisioningAuditEvents",
                column: "EnrollmentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProvisioningAudit_Actor",
                table: "ProvisioningAuditEvents",
                sql: "(\"ActorKind\" = 1 AND \"ActorUserId\" IS NOT NULL AND \"ActorDeviceId\" IS NULL) OR (\"ActorKind\" IN (2,3) AND \"ActorUserId\" IS NULL AND \"ActorDeviceId\" IS NOT NULL) OR (\"ActorKind\" = 4 AND \"ActorUserId\" IS NULL AND \"ActorDeviceId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_CertificateSha256",
                table: "DeviceCredentials",
                column: "CertificateSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_DeviceId",
                table: "DeviceCredentials",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_EnrollmentId",
                table: "DeviceCredentials",
                column: "EnrollmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_IssuanceOperationId",
                table: "DeviceCredentials",
                column: "IssuanceOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_LineId",
                table: "DeviceCredentials",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_State_LeaseUntilUtc",
                table: "DeviceCredentials",
                columns: new[] { "State", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEnrollments_ApprovedByUserId",
                table: "DeviceEnrollments",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEnrollments_CreatedByUserId",
                table: "DeviceEnrollments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEnrollments_LineId",
                table: "DeviceEnrollments",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "UX_Enrollment_ApprovalOperation",
                table: "DeviceEnrollments",
                column: "ApprovalOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Enrollment_OpenDevice",
                table: "DeviceEnrollments",
                column: "DeviceId",
                unique: true,
                filter: "\"State\" IN (1, 2, 3, 4, 5, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_Challenge_RequestPurposeExpiry",
                table: "EnrollmentChallenges",
                columns: new[] { "EnrollmentId", "Purpose", "ExpiresAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProvisioningAudit_ActorDevice",
                table: "ProvisioningAuditEvents",
                column: "ActorDeviceId",
                principalTable: "RegisteredDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProvisioningAudit_Credential",
                table: "ProvisioningAuditEvents",
                column: "CredentialId",
                principalTable: "DeviceCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProvisioningAudit_Enrollment",
                table: "ProvisioningAuditEvents",
                column: "EnrollmentId",
                principalTable: "DeviceEnrollments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Preserve enrollment and audit history; restore a verified backup or use a reviewed forward repair.");
    }
}
