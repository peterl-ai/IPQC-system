using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205SharedTaskBasicInformationDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskHeaderOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Field = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(24)" : "TEXT", maxLength: 24, nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: true),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    BaseDraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    OriginLocalEditVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    PredecessorOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PredecessorHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    CanonicalPayload = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    ReceiptJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskHeaderOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_Header_PredecessorScope", x => new { x.TaskId, x.OperationId, x.Field, x.ContentHash });
                    table.UniqueConstraint("AK_Header_ProjectionScope", x => new { x.TaskId, x.OperationId, x.Field });
                    table.CheckConstraint("CK_Header_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1 AND \"BaseDraftVersion\">=0 AND \"OriginLocalEditVersion\">=0 AND \"DraftVersion\">0 AND \"DraftVersion\"=\"BaseDraftVersion\"+1 AND \"Field\" IN ('Customer','Production','ProductMo') AND (\"Value\" IS NULL OR length(\"Value\")<=200) AND length(\"ContentHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND ((\"Kind\"='CaptureBasicInformationField' AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL) OR (\"Kind\"='CorrectBasicInformationField' AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64))");
                    table.ForeignKey(
                        name: "FK_Header_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Header_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Header_Contributor",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Header_Draft",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Header_Predecessor",
                        columns: x => new { x.TaskId, x.PredecessorOperationId, x.Field, x.PredecessorHash },
                        principalTable: "TaskHeaderOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "Field", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Header_Source",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskHeaderProjections",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Field = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(24)" : "TEXT", maxLength: 24, nullable: false),
                    LastOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskHeaderProjections", x => new { x.TaskId, x.Field });
                    table.ForeignKey(
                        name: "FK_HeaderProjection_Operation",
                        columns: x => new { x.TaskId, x.LastOperationId, x.Field },
                        principalTable: "TaskHeaderOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "Field" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Header_Batch",
                table: "TaskHeaderOperations",
                columns: new[] { "TaskId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Header_Version",
                table: "TaskHeaderOperations",
                columns: new[] { "TaskId", "DraftVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskHeaderOperations_ActorUserId",
                table: "TaskHeaderOperations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskHeaderOperations_ContributorUserId",
                table: "TaskHeaderOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskHeaderOperations_SourceWorkContextId",
                table: "TaskHeaderOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_Header_Predecessor",
                table: "TaskHeaderOperations",
                columns: new[] { "TaskId", "PredecessorOperationId", "Field", "PredecessorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskHeaderProjections_TaskId_LastOperationId_Field",
                table: "TaskHeaderProjections",
                columns: new[] { "TaskId", "LastOperationId", "Field" });
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Accepted Task Basic Information history cannot be downgraded.");
    }
}
