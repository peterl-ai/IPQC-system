using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3204TypedSharedDraftAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedDrafts",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedDrafts", x => x.TaskId);
                    table.CheckConstraint("CK_SharedDrafts_Version", "\"DraftVersion\" >= 0");
                    table.ForeignKey(
                        name: "FK_SharedDrafts_PatrolTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "PatrolTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceptedBatchReceipts",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    RequestFingerprint = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    ReceiptJson = table.Column<string>(type: "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptedBatchReceipts", x => new { x.TaskId, x.BatchId });
                    table.CheckConstraint("CK_AcceptedBatchReceipts_Version", "\"DraftVersion\" > 0 AND length(\"RequestFingerprint\") = 64 AND length(\"ReceiptJson\") > 0");
                    table.ForeignKey(
                        name: "FK_AcceptedBatchReceipts_SharedDrafts_TaskId",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SharedSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ClientSampleId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SequenceNo = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedSamples", x => x.Id);
                    table.UniqueConstraint("AK_SharedSamples_Id_TaskId", x => new { x.Id, x.TaskId });
                    table.UniqueConstraint("AK_SharedSamples_Id_TaskId_TaskItemId_InspectionAttemptId", x => new { x.Id, x.TaskId, x.TaskItemId, x.InspectionAttemptId });
                    table.CheckConstraint("CK_SharedSamples_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000' AND \"ClientSampleId\" <> '00000000-0000-0000-0000-000000000000' AND \"SequenceNo\" >= 1");
                    table.ForeignKey(
                        name: "FK_SharedSamples_AttemptScope",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SharedSamples_SharedDrafts_TaskId",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SharedAcceptanceAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    BeforeVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    AfterVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    NewOperationCount = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedAcceptanceAudits", x => x.Id);
                    table.CheckConstraint("CK_SharedAcceptanceAudits_Version", "\"BeforeVersion\" >= 0 AND \"AfterVersion\" = \"BeforeVersion\" + 1 AND \"NewOperationCount\" BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "FK_SharedAcceptanceAudits_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SharedAcceptanceAudits_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SampleId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadedPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionOperations", x => new { x.TaskId, x.OperationId });
                    table.UniqueConstraint("AK_ContributionOperations_TaskId_OperationId_ContentHash", x => new { x.TaskId, x.OperationId, x.ContentHash });
                    table.UniqueConstraint("AK_ContributionOperations_TaskId_OperationId_SampleId", x => new { x.TaskId, x.OperationId, x.SampleId });
                    table.CheckConstraint("CK_ContributionOperations_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"Kind\" IN ('CaptureSample','CorrectSample','RemoveSample') AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0");
                    table.ForeignKey(
                        name: "FK_ContributionOperations_ApplicationUsers_ContributorUserId",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionOperations_ApplicationUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionOperations_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionOperations_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionOperations_SampleScope",
                        columns: x => new { x.SampleId, x.TaskId, x.TaskItemId, x.InspectionAttemptId },
                        principalTable: "SharedSamples",
                        principalColumns: new[] { "Id", "TaskId", "TaskItemId", "InspectionAttemptId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionOperations_WorkContexts_SourceWorkContextId",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionDependencies",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    DependencyId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionDependencies", x => new { x.TaskId, x.OperationId, x.DependencyId });
                    table.CheckConstraint("CK_ContributionDependencies_NoSelf", "\"OperationId\" <> \"DependencyId\"");
                    table.ForeignKey(
                        name: "FK_ContributionDependencies_Operation",
                        columns: x => new { x.TaskId, x.OperationId },
                        principalTable: "ContributionOperations",
                        principalColumns: new[] { "TaskId", "OperationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionDependencies_Reference",
                        columns: x => new { x.TaskId, x.DependencyId, x.ContentHash },
                        principalTable: "ContributionOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "ContentHash" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SharedSampleProjections",
                columns: table => new
                {
                    SampleId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    LastOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ValueOperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionType = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(20)" : "TEXT", maxLength: 20, nullable: false),
                    InspectionValue = table.Column<decimal>(type: ActiveProvider.Contains("Npgsql") ? "numeric" : "TEXT", nullable: true),
                    Judgment = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(2)" : "TEXT", maxLength: 2, nullable: false),
                    Barcode = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: true),
                    IsRemoved = table.Column<bool>(type: ActiveProvider.Contains("Npgsql") ? "boolean" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedSampleProjections", x => x.SampleId);
                    table.CheckConstraint("CK_SharedSampleProjections_Value", "\"Judgment\" IN ('OK','NG') AND ((\"InspectionType\" = 'Qualitative' AND \"InspectionValue\" IS NULL AND \"Barcode\" IS NULL) OR (\"InspectionType\" = 'Quantitative' AND \"InspectionValue\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_SharedSampleProjections_LastOperation",
                        columns: x => new { x.TaskId, x.LastOperationId, x.SampleId },
                        principalTable: "ContributionOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "SampleId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SharedSampleProjections_Sample",
                        columns: x => new { x.SampleId, x.TaskId },
                        principalTable: "SharedSamples",
                        principalColumns: new[] { "Id", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SharedSampleProjections_ValueOperation",
                        columns: x => new { x.TaskId, x.ValueOperationId, x.SampleId },
                        principalTable: "ContributionOperations",
                        principalColumns: new[] { "TaskId", "OperationId", "SampleId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionDependencies_TaskId_DependencyId_ContentHash",
                table: "ContributionDependencies",
                columns: new[] { "TaskId", "DependencyId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionOperations_ContributorUserId",
                table: "ContributionOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionOperations_LineId",
                table: "ContributionOperations",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionOperations_SampleScope",
                table: "ContributionOperations",
                columns: new[] { "SampleId", "TaskId", "TaskItemId", "InspectionAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionOperations_SourceWorkContextId",
                table: "ContributionOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionOperations_TaskId_BatchId",
                table: "ContributionOperations",
                columns: new[] { "TaskId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionOperations_UploadedByUserId",
                table: "ContributionOperations",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedAcceptanceAudits_ActorUserId",
                table: "SharedAcceptanceAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedAcceptanceAudits_TaskId_BatchId",
                table: "SharedAcceptanceAudits",
                columns: new[] { "TaskId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedSampleProjections_SampleId_TaskId",
                table: "SharedSampleProjections",
                columns: new[] { "SampleId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedSampleProjections_TaskId_LastOperationId_SampleId",
                table: "SharedSampleProjections",
                columns: new[] { "TaskId", "LastOperationId", "SampleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedSampleProjections_TaskId_ValueOperationId_SampleId",
                table: "SharedSampleProjections",
                columns: new[] { "TaskId", "ValueOperationId", "SampleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedSamples_InspectionAttemptId_ClientSampleId",
                table: "SharedSamples",
                columns: new[] { "InspectionAttemptId", "ClientSampleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedSamples_InspectionAttemptId_SequenceNo",
                table: "SharedSamples",
                columns: new[] { "InspectionAttemptId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedSamples_InspectionAttemptId_TaskItemId",
                table: "SharedSamples",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedSamples_TaskId",
                table: "SharedSamples",
                column: "TaskId");
            AddProtection(migrationBuilder);
        }

        private void AddProtection(MigrationBuilder m)
        {
            const string sampleScope = "SELECT 1 FROM \"PatrolTaskItems\" i JOIN \"PatrolTasks\" t ON t.\"Id\"=i.\"PatrolTaskId\" WHERE i.\"Id\"=NEW.\"TaskItemId\" AND i.\"PatrolTaskId\"=NEW.\"TaskId\" AND t.\"WorkflowVersion\"=2";
            const string sourceScope = "SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2";
            string[] history = ["SharedSamples","ContributionOperations","ContributionDependencies","AcceptedBatchReceipts","SharedAcceptanceAudits"];
            if (ActiveProvider.Contains("Npgsql"))
            {
                m.Sql("CREATE FUNCTION ipqc_reject_accepted_history_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Accepted history is immutable' USING ERRCODE='23514'; END; $$;");
                foreach (var table in history)
                    m.Sql($"CREATE TRIGGER \"TR_{table}_Immutable\" BEFORE UPDATE OR DELETE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                m.Sql($"CREATE FUNCTION ipqc_shared_sample_scope() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NOT EXISTS ({sampleScope}) THEN RAISE EXCEPTION 'Shared sample scope mismatch' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
                m.Sql("CREATE TRIGGER \"TR_SharedSamples_Scope\" BEFORE INSERT ON \"SharedSamples\" FOR EACH ROW EXECUTE FUNCTION ipqc_shared_sample_scope();");
                m.Sql($"CREATE FUNCTION ipqc_shared_operation_scope() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NOT EXISTS ({sourceScope}) THEN RAISE EXCEPTION 'Shared source scope mismatch' USING ERRCODE='23514'; END IF; RETURN NEW; END; $$;");
                m.Sql("CREATE TRIGGER \"TR_ContributionOperations_SourceScope\" BEFORE INSERT ON \"ContributionOperations\" FOR EACH ROW EXECUTE FUNCTION ipqc_shared_operation_scope();");
            }
            else
            {
                foreach (var table in history)
                    foreach (var action in new[] { "UPDATE","DELETE" })
                        m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;");
                m.Sql($"CREATE TRIGGER \"TR_SharedSamples_Scope\" BEFORE INSERT ON \"SharedSamples\" WHEN NOT EXISTS ({sampleScope}) BEGIN SELECT RAISE(ABORT,'Shared sample scope mismatch'); END;");
                m.Sql($"CREATE TRIGGER \"TR_ContributionOperations_SourceScope\" BEFORE INSERT ON \"ContributionOperations\" WHEN NOT EXISTS ({sourceScope}) BEGIN SELECT RAISE(ABORT,'Shared source scope mismatch'); END;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain accepted draft history; use reviewed forward repair or verified backup restore.");
    }
}
