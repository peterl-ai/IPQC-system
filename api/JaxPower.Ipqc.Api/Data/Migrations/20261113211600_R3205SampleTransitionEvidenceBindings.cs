using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205SampleTransitionEvidenceBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SampleEvidenceOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BindingId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SampleId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SampleOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SampleHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    TransitionKind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    PhotoReferenceOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    AttachmentId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PhotoReferenceHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    PredecessorOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PredecessorHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: true),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleEvidenceOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_SampleEvidence_Predecessor", x => new { x.TaskId, x.OperationId, x.BindingId, x.SampleOperationId, x.ContentHash });
                    table.UniqueConstraint("AK_SampleEvidence_Projection", x => new { x.TaskId, x.OperationId, x.BindingId });
                    table.CheckConstraint("CK_SampleEvidence_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\"=2 AND \"CanonicalizationVersion\"=1 AND \"DraftVersion\">0 AND length(\"ContentHash\")=64 AND length(\"SampleHash\")=64 AND length(\"CanonicalPayload\")>0 AND length(\"ReceiptJson\")>0 AND \"TransitionKind\" IN ('InitialNGCapture','NGToOKCorrection','NGToNGCorrection','OKToNGCorrection') AND ((\"Kind\"='BindSampleEvidence' AND \"OperationId\"=\"BindingId\" AND \"PredecessorOperationId\" IS NULL AND \"PredecessorHash\" IS NULL) OR (\"Kind\" IN ('CorrectSampleEvidence','WithdrawSampleEvidence') AND \"OperationId\"<>\"BindingId\" AND \"PredecessorOperationId\" IS NOT NULL AND \"PredecessorHash\" IS NOT NULL AND length(\"PredecessorHash\")=64)) AND ((\"Kind\"='WithdrawSampleEvidence' AND \"PhotoReferenceOperationId\" IS NULL AND \"AttachmentId\" IS NULL AND \"PhotoReferenceHash\" IS NULL) OR (\"Kind\"<>'WithdrawSampleEvidence' AND \"PhotoReferenceOperationId\" IS NOT NULL AND \"AttachmentId\" IS NOT NULL AND \"PhotoReferenceHash\" IS NOT NULL AND length(\"PhotoReferenceHash\")=64))");
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Actor",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Contributor",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Photo",
                        columns: x => new { x.TaskId, x.PhotoReferenceOperationId, x.AttachmentId, x.PhotoReferenceHash },
                        principalTable: "DraftAttachmentOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "AttachmentId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Predecessor",
                        columns: x => new { x.TaskId, x.PredecessorOperationId, x.BindingId, x.SampleOperationId, x.PredecessorHash },
                        principalTable: "SampleEvidenceOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "BindingId", "SampleOperationId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Root",
                        columns: x => new { x.TaskId, x.BindingId },
                        principalTable: "SampleEvidenceOperations",
                        principalColumns: new[] { "TaskId", "OperationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Sample",
                        columns: x => new { x.SampleId, x.TaskId, x.TaskItemId, x.InspectionAttemptId },
                        principalTable: "SharedSamples",
                        principalColumns: new[] { "Id", "TaskId", "TaskItemId", "InspectionAttemptId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Source",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleEvidence_Target",
                        columns: x => new { x.TaskId, x.SampleOperationId, x.SampleHash },
                        principalTable: "ContributionOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SampleEvidenceProjections",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BindingId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LastOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: ActiveProvider.Contains("Npgsql") ? "boolean" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleEvidenceProjections", x => new { x.TaskId, x.BindingId });
                    table.ForeignKey(
                        name: "FK_SampleEvidenceProjection_Last",
                        columns: x => new { x.TaskId, x.LastOperationId, x.BindingId },
                        principalTable: "SampleEvidenceOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "BindingId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidence_Batch",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidence_History",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "DraftVersion", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidence_Item",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "TaskItemId", "InspectionAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidence_Photo",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "PhotoReferenceOperationId", "AttachmentId", "PhotoReferenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidence_Predecessor",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "PredecessorOperationId", "BindingId", "SampleOperationId", "PredecessorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidenceOperations_ActorUserId",
                table: "SampleEvidenceOperations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidenceOperations_ContributorUserId",
                table: "SampleEvidenceOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidence_Sample",
                table: "SampleEvidenceOperations",
                columns: new[] { "SampleId", "TaskId", "TaskItemId", "InspectionAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidenceOperations_SourceWorkContextId",
                table: "SampleEvidenceOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidenceOperations_TaskId_BindingId",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "BindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidenceOperations_TaskId_SampleOperationId_SampleHash",
                table: "SampleEvidenceOperations",
                columns: new[] { "TaskId", "SampleOperationId", "SampleHash" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleEvidenceProjection_Last",
                table: "SampleEvidenceProjections",
                columns: new[] { "TaskId", "LastOperationId", "BindingId" });
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Accepted sample evidence history cannot be downgraded.");
    }
}
