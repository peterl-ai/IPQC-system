using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205UnableToMeasureNgDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceptedOperationIdentities",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Domain = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptedOperationIdentities", x => new { x.TaskId, x.OperationId });
                    table.CheckConstraint("CK_AcceptedOperationIdentity_Domain", "length(\"Domain\") BETWEEN 1 AND 64");
                    table.ForeignKey(
                        name: "FK_AcceptedOperationIdentities_SharedDrafts_TaskId",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnableToMeasureOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    NgReason = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    EvidenceOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    EvidenceHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    PredecessorOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PredecessorHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnableToMeasureOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_Unable_PredecessorScope", x => new { x.TaskId, x.OperationId, x.InspectionAttemptId, x.ContentHash });
                    table.UniqueConstraint("AK_Unable_ProjectionScope", x => new { x.TaskId, x.OperationId, x.InspectionAttemptId, x.TaskItemId });
                    table.CheckConstraint("CK_Unable_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0 AND \"Kind\" IN ('SetUnableToMeasureNG','CorrectUnableToMeasureNG','ClearUnableToMeasureNG') AND ((\"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL AND \"Kind\"='SetUnableToMeasureNG') OR (\"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64)) AND ((\"Kind\"='ClearUnableToMeasureNG' AND \"NgReason\" IS NULL AND \"EvidenceOperationId\" IS NULL AND \"AttachmentId\" IS NULL AND \"EvidenceHash\" IS NULL) OR (\"Kind\"<>'ClearUnableToMeasureNG' AND \"NgReason\" IS NOT NULL AND length(trim(\"NgReason\")) BETWEEN 1 AND 2000 AND \"EvidenceOperationId\" IS NOT NULL AND \"AttachmentId\" IS NOT NULL AND \"EvidenceHash\" IS NOT NULL AND length(\"EvidenceHash\")=64))");
                    table.ForeignKey(
                        name: "FK_Unable_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Unable_Attempt",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Unable_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Unable_Contributor",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Unable_Evidence",
                        columns: x => new { x.TaskId, x.EvidenceOperationId, x.AttachmentId, x.EvidenceHash },
                        principalTable: "DraftAttachmentOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "AttachmentId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Unable_Predecessor",
                        columns: x => new { x.TaskId, x.PredecessorOperationId, x.InspectionAttemptId, x.PredecessorHash },
                        principalTable: "UnableToMeasureOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "InspectionAttemptId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Unable_Source",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnableToMeasureProjections",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LastOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: ActiveProvider.Contains("Npgsql") ? "boolean" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnableToMeasureProjections", x => new { x.TaskId, x.InspectionAttemptId });
                    table.ForeignKey(
                        name: "FK_UnableProjection_Operation",
                        columns: x => new { x.TaskId, x.LastOperationId, x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "UnableToMeasureOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "InspectionAttemptId", "TaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Unable_Batch",
                table: "UnableToMeasureOperations",
                columns: new[] { "TaskId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Unable_Evidence",
                table: "UnableToMeasureOperations",
                columns: new[] { "TaskId", "EvidenceOperationId", "AttachmentId", "EvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Unable_History",
                table: "UnableToMeasureOperations",
                columns: new[] { "TaskId", "DraftVersion", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Unable_Predecessor",
                table: "UnableToMeasureOperations",
                columns: new[] { "TaskId", "PredecessorOperationId", "InspectionAttemptId", "PredecessorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_UnableToMeasureOperations_ActorUserId",
                table: "UnableToMeasureOperations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnableToMeasureOperations_ContributorUserId",
                table: "UnableToMeasureOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnableToMeasureOperations_InspectionAttemptId_TaskItemId",
                table: "UnableToMeasureOperations",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnableToMeasureOperations_SourceWorkContextId",
                table: "UnableToMeasureOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_UnableProjection_Operation",
                table: "UnableToMeasureProjections",
                columns: new[] { "TaskId", "LastOperationId", "InspectionAttemptId", "TaskItemId" });
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain disposition and evidence history; use reviewed forward repair or verified restore.");
    }
}
