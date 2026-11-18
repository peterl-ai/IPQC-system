using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205OrdinaryNgDetailsDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider.Contains("Npgsql");
            migrationBuilder.CreateTable(
                name: "OrdinaryNgDetailsOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SourceWorkContextId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: postgres ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: postgres ? "character varying(32)" : "TEXT", maxLength: 32, nullable: false),
                    BaseDraftVersion = table.Column<long>(type: postgres ? "bigint" : "INTEGER", nullable: false),
                    OriginLocalEditVersion = table.Column<long>(type: postgres ? "bigint" : "INTEGER", nullable: false),
                    PredecessorOperationId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: true),
                    PredecessorHash = table.Column<string>(type: postgres ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    NgType = table.Column<string>(type: postgres ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    NgReason = table.Column<string>(type: postgres ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    Remarks = table.Column<string>(type: postgres ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    NgBasisJson = table.Column<string>(type: postgres ? "text" : "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: postgres ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: postgres ? "text" : "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: postgres ? "text" : "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: postgres ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdinaryNgDetailsOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_OrdNg_Predecessor", x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId, x.ContentHash });
                    table.CheckConstraint("CK_OrdNg_Operation", "\"OperationId\"<>'00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1\nAND \"BaseDraftVersion\">=0 AND \"OriginLocalEditVersion\">=0 AND \"DraftVersion\">0 AND \"DraftVersion\"=\"BaseDraftVersion\"+1\nAND length(\"ContentHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND length(\"NgBasisJson\")>0\nAND ((\"Kind\"='SetOrdinaryNgDetails' AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL)\n     OR (\"Kind\"='CorrectOrdinaryNgDetails' AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64))\nAND (\"NgType\" IS NULL OR length(\"NgType\")<=2000) AND (\"NgReason\" IS NULL OR length(\"NgReason\")<=2000)\nAND (\"Remarks\" IS NULL OR length(\"Remarks\")<=2000)");
                    table.ForeignKey(
                        name: "FK_OrdNg_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdNg_Attempt",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdNg_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdNg_Contributor",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdNg_Draft",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdNg_Predecessor",
                        columns: x => new { x.TaskId, x.PredecessorOperationId, x.TaskItemId, x.InspectionAttemptId, x.PredecessorHash },
                        principalTable: "OrdinaryNgDetailsOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "TaskItemId", "InspectionAttemptId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrdNg_Source",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrdinaryNgDetailsOperations_ActorUserId",
                table: "OrdinaryNgDetailsOperations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdinaryNgDetailsOperations_ContributorUserId",
                table: "OrdinaryNgDetailsOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdinaryNgDetailsOperations_InspectionAttemptId_TaskItemId",
                table: "OrdinaryNgDetailsOperations",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrdinaryNgDetailsOperations_SourceWorkContextId",
                table: "OrdinaryNgDetailsOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdNg_Batch",
                table: "OrdinaryNgDetailsOperations",
                columns: new[] { "TaskId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdNg_Predecessor",
                table: "OrdinaryNgDetailsOperations",
                columns: new[] { "TaskId", "PredecessorOperationId", "TaskItemId", "InspectionAttemptId", "PredecessorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_OrdNg_Scope",
                table: "OrdinaryNgDetailsOperations",
                columns: new[] { "TaskId", "TaskItemId", "InspectionAttemptId", "DraftVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_OrdNg_Version",
                table: "OrdinaryNgDetailsOperations",
                columns: new[] { "TaskId", "DraftVersion" },
                unique: true);
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Accepted ordinary NG details cannot be downgraded.");
    }
}
