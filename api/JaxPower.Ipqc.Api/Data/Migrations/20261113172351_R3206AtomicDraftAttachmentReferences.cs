using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3206AtomicDraftAttachmentReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftAttachmentOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ClientPhotoId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    PhotoSourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PhotoContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PhotoUploadedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PhotoUploadPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    PredecessorOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PredecessorHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftAttachmentOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_DraftPhoto_OperationAttachment", x => new { x.TaskId, x.OperationId, x.AttachmentId });
                    table.UniqueConstraint("AK_DraftPhoto_PredecessorScope", x => new { x.TaskId, x.OperationId, x.AttachmentId, x.ContentHash });
                    table.CheckConstraint("CK_DraftPhoto_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"Kind\" IN ('AssociateAttachment','DetachAttachment') AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"Sha256\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0 AND ((\"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL AND \"Kind\" = 'AssociateAttachment') OR (\"PredecessorOperationId\" IS NOT NULL AND length(\"PredecessorHash\") = 64))");
                    table.ForeignKey(
                        name: "FK_DraftPhoto_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftPhoto_Attempt",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftPhoto_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftPhoto_Contributor",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftPhoto_Predecessor",
                        columns: x => new { x.TaskId, x.PredecessorOperationId, x.AttachmentId, x.PredecessorHash },
                        principalTable: "DraftAttachmentOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "AttachmentId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftPhoto_Source",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftPhoto_StorageAcceptance",
                        column: x => x.AttachmentId,
                        principalTable: "AttachmentAcceptances",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DraftOperationKeys",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Domain = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(20)" : "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftOperationKeys", x => new { x.TaskId, x.OperationId });
                    table.CheckConstraint("CK_DraftOperationKeys_Domain", "\"Domain\" IN ('Sample','ItemRemoval','AttachmentReference')");
                    table.ForeignKey(
                        name: "FK_DraftOperationKeys_SharedDrafts_TaskId",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DraftAttachmentProjections",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LastOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    IsAttached = table.Column<bool>(type: ActiveProvider.Contains("Npgsql") ? "boolean" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftAttachmentProjections", x => new { x.TaskId, x.AttachmentId });
                    table.ForeignKey(
                        name: "FK_DraftPhotoProjection_Operation",
                        columns: x => new { x.TaskId, x.LastOperationId, x.AttachmentId },
                        principalTable: "DraftAttachmentOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "AttachmentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachmentOperations_ActorUserId",
                table: "DraftAttachmentOperations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachmentOperations_AttachmentId",
                table: "DraftAttachmentOperations",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachmentOperations_ContributorUserId",
                table: "DraftAttachmentOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachmentOperations_InspectionAttemptId_TaskItemId",
                table: "DraftAttachmentOperations",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachmentOperations_SourceWorkContextId",
                table: "DraftAttachmentOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftAttachmentOperations_TaskId_BatchId",
                table: "DraftAttachmentOperations",
                columns: new[] { "TaskId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftPhoto_Predecessor",
                table: "DraftAttachmentOperations",
                columns: new[] { "TaskId", "PredecessorOperationId", "AttachmentId", "PredecessorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftPhotoProjection_Last",
                table: "DraftAttachmentProjections",
                columns: new[] { "TaskId", "LastOperationId", "AttachmentId" });
            migrationBuilder.CreateIndex(name: "IX_DraftPhoto_History", table: "DraftAttachmentOperations", columns: new[] { "TaskId", "DraftVersion", "OperationId" });
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain accepted references and files; use reviewed forward repair or verified restore.");
    }
}
