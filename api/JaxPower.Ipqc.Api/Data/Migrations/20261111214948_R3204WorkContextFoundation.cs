using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3204WorkContextFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContractVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    ProofSecretHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    UserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    IssuedPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    IssuedAuthVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    CaptureUntilUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    PersonalSessionExpiresAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    SecurityRevokedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: true),
                    SecurityRevocationReason = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    SecurityRevokedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    Revision = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkContexts", x => x.Id);
                    table.CheckConstraint("CK_WorkContexts_Capture", "\"IssuedAtUtc\" < \"CaptureUntilUtc\" AND \"CaptureUntilUtc\" <= \"PersonalSessionExpiresAtUtc\"");
                    table.CheckConstraint("CK_WorkContexts_Contract", "\"ContractVersion\" = 1 AND \"WorkflowVersion\" = 2 AND \"Revision\" >= 1 AND length(\"ProofSecretHash\") = 64 AND length(trim(\"EnvironmentId\")) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_WorkContexts_Revocation", "(\"SecurityRevokedAtUtc\" IS NULL AND \"SecurityRevocationReason\" IS NULL AND \"SecurityRevokedByUserId\" IS NULL) OR (\"SecurityRevokedAtUtc\" IS NOT NULL AND \"SecurityRevocationReason\" IS NOT NULL AND length(trim(\"SecurityRevocationReason\")) > 0 AND \"SecurityRevokedByUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkContexts_ApplicationUsers_SecurityRevokedByUserId",
                        column: x => x.SecurityRevokedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkContexts_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkContexts_PatrolTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "PatrolTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkContexts_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkContextAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ChangeId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Action = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(80)" : "TEXT", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    ActorType = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(20)" : "TEXT", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUsername = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    SourceUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Reason = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    BeforeJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    BeforeRevision = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: true),
                    AfterRevision = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkContextAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_WorkContextAuditEvents_Action", "\"Action\" IN ('ContextIssued','ContextSecurityRevoked') AND \"ActorType\" = 'User' AND \"AfterRevision\" >= 1");
                    table.ForeignKey(
                        name: "FK_WorkContextAuditEvents_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkContextAuditEvents_ApplicationUsers_SourceUserId",
                        column: x => x.SourceUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkContextAuditEvents_WorkContexts_WorkContextId",
                        column: x => x.WorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkContextAuditEvents_ActorUserId",
                table: "WorkContextAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkContextAuditEvents_ChangeId",
                table: "WorkContextAuditEvents",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkContextAuditEvents_SourceUserId",
                table: "WorkContextAuditEvents",
                column: "SourceUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkContextAuditEvents_WorkContextId_OccurredAtUtc_Id",
                table: "WorkContextAuditEvents",
                columns: new[] { "WorkContextId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkContexts_EnvironmentId_TaskId",
                table: "WorkContexts",
                columns: new[] { "EnvironmentId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkContexts_LineId",
                table: "WorkContexts",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkContexts_SecurityRevokedByUserId",
                table: "WorkContexts",
                column: "SecurityRevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkContexts_TaskId_UserId",
                table: "WorkContexts",
                columns: new[] { "TaskId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkContexts_UserId_SecurityRevokedAtUtc",
                table: "WorkContexts",
                columns: new[] { "UserId", "SecurityRevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain WorkContext provenance and audit; use reviewed forward repair or verified backup.");
        }
    }
}
