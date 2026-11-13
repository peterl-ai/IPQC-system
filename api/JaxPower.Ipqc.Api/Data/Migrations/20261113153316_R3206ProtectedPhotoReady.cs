using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3206ProtectedPhotoReady : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ClientPhotoId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ExpectedBytes = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    ExpectedSha256 = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    DeclaredContentType = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(20)" : "TEXT", maxLength: 20, nullable: false),
                    CaptureSource = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(10)" : "TEXT", maxLength: 10, nullable: false),
                    StorageKey = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(32)" : "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionAttachments", x => x.Id);
                    table.CheckConstraint("CK_Attachment_Identity", "\"WorkflowVersion\" = 2 AND \"ExpectedBytes\" BETWEEN 1 AND 5242880 AND length(\"ExpectedSha256\") = 64 AND length(\"StorageKey\") = 32 AND length(\"EnvironmentId\") BETWEEN 1 AND 200 AND \"DeclaredContentType\" IN ('image/jpeg','image/png') AND \"CaptureSource\" IN ('Camera','Album') AND \"ClientPhotoId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_InspectionAttachments_ApplicationUsers_ContributorUserId",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Photo_AttemptScope",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionAttachments_PatrolTaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "PatrolTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionAttachments_PatrolTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "PatrolTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionAttachments_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionAttachments_WorkContexts_SourceWorkContextId",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttachmentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    State = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(30)" : "TEXT", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttachmentEvents_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttachmentEvents_InspectionAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "InspectionAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttachmentUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StageKey = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(32)" : "TEXT", maxLength: 32, nullable: false),
                    StartedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StartedPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    StoredAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentUploads", x => x.Id);
                    table.UniqueConstraint("AK_AttachmentUploads_Id_AttachmentId", x => new { x.Id, x.AttachmentId });
                    table.CheckConstraint("CK_AttachmentUpload_Stored", "length(\"StageKey\") = 32 AND (\"StoredAtUtc\" IS NULL OR \"StoredAtUtc\" >= \"StartedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_AttachmentUploads_ApplicationUsers_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttachmentUploads_InspectionAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "InspectionAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttachmentAcceptances",
                columns: table => new
                {
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadedPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ByteCount = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(20)" : "TEXT", maxLength: 20, nullable: false),
                    StoredEncoding = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(10)" : "TEXT", maxLength: 10, nullable: false),
                    Width = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    Height = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    StoredAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    ReadyAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentAcceptances", x => x.AttachmentId);
                    table.CheckConstraint("CK_AttachmentAcceptance_Ready", "\"ByteCount\" BETWEEN 1 AND 5242880 AND length(\"Sha256\") = 64 AND \"Width\" BETWEEN 1 AND 6000 AND \"Height\" BETWEEN 1 AND 6000 AND CAST(\"Width\" AS bigint) * \"Height\" <= 12000000 AND \"ReadyAtUtc\" >= \"StoredAtUtc\" AND ((\"ContentType\" = 'image/jpeg' AND \"StoredEncoding\" = 'JPEG') OR (\"ContentType\" = 'image/png' AND \"StoredEncoding\" = 'PNG'))");
                    table.ForeignKey(
                        name: "FK_AttachmentAcceptances_ApplicationUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhotoAcceptance_UploadScope",
                        columns: x => new { x.UploadId, x.AttachmentId },
                        principalTable: "AttachmentUploads",
                        principalColumns: new[] { "Id", "AttachmentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttachmentAcceptances_InspectionAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "InspectionAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttachmentAvailabilities",
                columns: table => new
                {
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    State = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(30)" : "TEXT", maxLength: 30, nullable: false),
                    Version = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    ActiveUploadId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    LeaseToken = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: true),
                    AcceptedAttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentAvailabilities", x => x.AttachmentId);
                    table.CheckConstraint("CK_AttachmentAvailability_State", "\"Version\" >= 0 AND \"State\" IN ('AwaitingBytes','Receiving','Stored','Ready','Failed','MissingUnexpected','Corrupt') AND (\"State\" <> 'Ready' OR \"AcceptedAttachmentId\" IS NOT NULL) AND (\"AcceptedAttachmentId\" IS NULL OR \"AcceptedAttachmentId\" = \"AttachmentId\") AND ((\"LeaseToken\" IS NULL AND \"LeaseUntilUtc\" IS NULL) OR (\"LeaseToken\" IS NOT NULL AND \"LeaseUntilUtc\" IS NOT NULL AND \"ActiveUploadId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_PhotoAvailability_Acceptance",
                        column: x => x.AcceptedAttachmentId,
                        principalTable: "AttachmentAcceptances",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhotoAvailability_UploadScope",
                        columns: x => new { x.ActiveUploadId, x.AttachmentId },
                        principalTable: "AttachmentUploads",
                        principalColumns: new[] { "Id", "AttachmentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttachmentAvailabilities_InspectionAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "InspectionAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentAcceptances_UploadedByUserId",
                table: "AttachmentAcceptances",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentAcceptances_UploadId_AttachmentId",
                table: "AttachmentAcceptances",
                columns: new[] { "UploadId", "AttachmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentAvailabilities_AcceptedAttachmentId",
                table: "AttachmentAvailabilities",
                column: "AcceptedAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentAvailabilities_ActiveUploadId_AttachmentId",
                table: "AttachmentAvailabilities",
                columns: new[] { "ActiveUploadId", "AttachmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentEvents_ActorUserId",
                table: "AttachmentEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentEvents_AttachmentId",
                table: "AttachmentEvents",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentUploads_AttachmentId",
                table: "AttachmentUploads",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentUploads_StageKey",
                table: "AttachmentUploads",
                column: "StageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentUploads_StartedByUserId",
                table: "AttachmentUploads",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_ContributorUserId",
                table: "InspectionAttachments",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_EnvironmentId_ClientPhotoId",
                table: "InspectionAttachments",
                columns: new[] { "EnvironmentId", "ClientPhotoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_InspectionAttemptId_TaskItemId",
                table: "InspectionAttachments",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_LineId",
                table: "InspectionAttachments",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_SourceWorkContextId",
                table: "InspectionAttachments",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_StorageKey",
                table: "InspectionAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_TaskId",
                table: "InspectionAttachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttachments_TaskItemId",
                table: "InspectionAttachments",
                column: "TaskItemId");
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Preserve attachment identity and accepted history; use reviewed forward repair or verified restore.");
    }
}
