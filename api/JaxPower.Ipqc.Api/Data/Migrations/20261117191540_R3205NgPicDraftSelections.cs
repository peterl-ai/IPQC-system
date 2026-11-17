using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205NgPicDraftSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NgPicSelectionOperations",
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
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(32)" : "TEXT", maxLength: 32, nullable: false),
                    BaseDraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    OriginLocalEditVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    PredecessorOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PredecessorHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    PicCount = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    NgKind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(32)" : "TEXT", maxLength: 32, nullable: false),
                    NgBasisJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NgPicSelectionOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_NgPic_Predecessor", x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId, x.ContentHash });
                    table.UniqueConstraint("AK_NgPic_Projection", x => new { x.TaskId, x.OperationId, x.TaskItemId, x.InspectionAttemptId });
                    table.CheckConstraint("CK_NgPic_Operation", "\"OperationId\"<>'00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1 AND \"BaseDraftVersion\">=0 AND \"OriginLocalEditVersion\">=0 AND \"DraftVersion\">0 AND \"DraftVersion\"=\"BaseDraftVersion\"+1 AND \"PicCount\" BETWEEN 0 AND 100 AND \"NgKind\" IN ('OrdinaryNG','UnableToMeasureNG') AND length(\"NgBasisJson\")>0 AND length(\"ContentHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND ((\"Kind\"='SelectNgPics' AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL) OR (\"Kind\"='CorrectNgPics' AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64))");
                    table.ForeignKey(
                        name: "FK_NgPic_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPic_Attempt",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPic_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPic_Contributor",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPic_Draft",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPic_Predecessor",
                        columns: x => new { x.TaskId, x.PredecessorOperationId, x.TaskItemId, x.InspectionAttemptId, x.PredecessorHash },
                        principalTable: "NgPicSelectionOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "TaskItemId", "InspectionAttemptId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPic_Source",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NgPicSelectionMembers",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PicId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    RosterRevision = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    Name = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NgPicSelectionMembers", x => new { x.TaskId, x.OperationId, x.PicId });
                    table.CheckConstraint("CK_NgPicMember_Snapshot", "\"RosterRevision\">0 AND length(\"Name\") BETWEEN 1 AND 200 AND length(\"Department\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_NgPicMember_Operation",
                        columns: x => new { x.TaskId, x.OperationId },
                        principalTable: "NgPicSelectionOperations",
                        principalColumns: new[] { "TaskId", "OperationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NgPicMember_Roster",
                        columns: x => new { x.PicId, x.RosterRevision },
                        principalTable: "PicRosterRevisions",
                        principalColumns: new[] { "PicId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NgPicSelectionProjections",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LastOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NgPicSelectionProjections", x => new { x.TaskId, x.TaskItemId, x.InspectionAttemptId });
                    table.ForeignKey(
                        name: "FK_NgPicProjection_Operation",
                        columns: x => new { x.TaskId, x.LastOperationId, x.TaskItemId, x.InspectionAttemptId },
                        principalTable: "NgPicSelectionOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "TaskItemId", "InspectionAttemptId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NgPicSelectionMembers_PicId_RosterRevision",
                table: "NgPicSelectionMembers",
                columns: new[] { "PicId", "RosterRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_NgPic_Batch",
                table: "NgPicSelectionOperations",
                columns: new[] { "TaskId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NgPic_Predecessor",
                table: "NgPicSelectionOperations",
                columns: new[] { "TaskId", "PredecessorOperationId", "TaskItemId", "InspectionAttemptId", "PredecessorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_NgPic_Version",
                table: "NgPicSelectionOperations",
                columns: new[] { "TaskId", "DraftVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NgPicSelectionOperations_ActorUserId",
                table: "NgPicSelectionOperations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NgPicSelectionOperations_ContributorUserId",
                table: "NgPicSelectionOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NgPicSelectionOperations_InspectionAttemptId_TaskItemId",
                table: "NgPicSelectionOperations",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_NgPicSelectionOperations_SourceWorkContextId",
                table: "NgPicSelectionOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_NgPicProjection_Last",
                table: "NgPicSelectionProjections",
                columns: new[] { "TaskId", "LastOperationId", "TaskItemId", "InspectionAttemptId" });
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Accepted NG PIC history cannot be downgraded.");
    }
}
