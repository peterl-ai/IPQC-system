using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205PerItemRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemRemovalOperations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SourceWorkContextId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContributorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    UploadedPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    WorkflowVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    CanonicalPayload = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiptJson = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    DraftVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemRemovalOperations", x => new { x.TaskId, x.OperationId });
                    table.CheckConstraint("CK_ItemRemovals_Contract", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000' AND \"WorkflowVersion\" = 2 AND \"CanonicalizationVersion\" = 1 AND \"Kind\" = 'RemoveItem' AND \"DraftVersion\" > 0 AND length(\"ContentHash\") = 64 AND length(\"CanonicalPayload\") > 0 AND length(\"ReceiptJson\") > 0");
                    table.ForeignKey(
                        name: "FK_ItemRemovalOperations_ApplicationUsers_ContributorUserId",
                        column: x => x.ContributorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemRemovalOperations_ApplicationUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemRemovalOperations_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemRemovalOperations_WorkContexts_SourceWorkContextId",
                        column: x => x.SourceWorkContextId,
                        principalTable: "WorkContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemRemovals_Attempt",
                        columns: x => new { x.InspectionAttemptId, x.TaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemRemovals_Batch",
                        columns: x => new { x.TaskId, x.BatchId },
                        principalTable: "AcceptedBatchReceipts",
                        principalColumns: new[] { "TaskId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SharedOperationIdentities",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Domain = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(20)" : "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedOperationIdentities", x => new { x.TaskId, x.OperationId });
                    table.CheckConstraint("CK_SharedOperationIdentities_Domain", "\"Domain\" IN ('Sample','ItemRemoval')");
                    table.ForeignKey(
                        name: "FK_SharedOperationIdentities_SharedDrafts_TaskId",
                        column: x => x.TaskId,
                        principalTable: "SharedDrafts",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_ContributorUserId",
                table: "ItemRemovalOperations",
                column: "ContributorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_InspectionAttemptId",
                table: "ItemRemovalOperations",
                column: "InspectionAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_InspectionAttemptId_TaskItemId",
                table: "ItemRemovalOperations",
                columns: new[] { "InspectionAttemptId", "TaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_LineId",
                table: "ItemRemovalOperations",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_SourceWorkContextId",
                table: "ItemRemovalOperations",
                column: "SourceWorkContextId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_TaskId_BatchId",
                table: "ItemRemovalOperations",
                columns: new[] { "TaskId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemRemovalOperations_UploadedByUserId",
                table: "ItemRemovalOperations",
                column: "UploadedByUserId");
            // Derive only an ID uniqueness index from actual existing accepted operations.
            // No operation, receipt, sample, actor or acceptance timestamp is backfilled.
            migrationBuilder.Sql("INSERT INTO \"SharedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") SELECT \"TaskId\",\"OperationId\",'Sample' FROM \"ContributionOperations\";");
            Protect(migrationBuilder);
        }

        private void Protect(MigrationBuilder m)
        {
            const string scope = "SELECT 1 FROM \"WorkContexts\" c JOIN \"PatrolTasks\" t ON t.\"Id\"=c.\"TaskId\" JOIN \"PatrolTaskItems\" i ON i.\"PatrolTaskId\"=t.\"Id\" WHERE c.\"Id\"=NEW.\"SourceWorkContextId\" AND c.\"TaskId\"=NEW.\"TaskId\" AND c.\"UserId\"=NEW.\"ContributorUserId\" AND c.\"EnvironmentId\"=NEW.\"EnvironmentId\" AND c.\"LineId\"=NEW.\"LineId\" AND c.\"WorkflowVersion\"=NEW.\"WorkflowVersion\" AND t.\"LineId\"=NEW.\"LineId\" AND t.\"WorkflowVersion\"=2 AND i.\"Id\"=NEW.\"TaskItemId\"";
            const string removed = "SELECT 1 FROM \"ItemRemovalOperations\" r WHERE r.\"TaskId\"=NEW.\"TaskId\" AND r.\"InspectionAttemptId\"=NEW.\"InspectionAttemptId\"";
            const string sampleIndex = "INSERT INTO \"SharedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'Sample');";
            const string itemIndex = "INSERT INTO \"SharedOperationIdentities\" (\"TaskId\",\"OperationId\",\"Domain\") VALUES (NEW.\"TaskId\",NEW.\"OperationId\",'ItemRemoval');";
            if (ActiveProvider.Contains("Npgsql")) {
                foreach (var table in new[] { "ItemRemovalOperations", "SharedOperationIdentities" })
                    m.Sql($"CREATE TRIGGER \"TR_{table}_Immutable\" BEFORE UPDATE OR DELETE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION ipqc_reject_accepted_history_change();");
                m.Sql($"CREATE FUNCTION ipqc_index_sample_operation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF EXISTS ({removed}) THEN RAISE EXCEPTION 'Draft item is Removed' USING ERRCODE='23514'; END IF; {sampleIndex} RETURN NEW; END; $$;");
                m.Sql("CREATE TRIGGER \"TR_ContributionOperations_Identity\" BEFORE INSERT ON \"ContributionOperations\" FOR EACH ROW EXECUTE FUNCTION ipqc_index_sample_operation();");
                m.Sql($"CREATE FUNCTION ipqc_index_item_removal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NOT EXISTS ({scope}) THEN RAISE EXCEPTION 'Shared item scope mismatch' USING ERRCODE='23514'; END IF; {itemIndex} RETURN NEW; END; $$;");
                m.Sql("CREATE TRIGGER \"TR_ItemRemovalOperations_ScopeIdentity\" BEFORE INSERT ON \"ItemRemovalOperations\" FOR EACH ROW EXECUTE FUNCTION ipqc_index_item_removal();");
            } else {
                foreach (var table in new[] { "ItemRemovalOperations", "SharedOperationIdentities" })
                    foreach (var action in new[] { "UPDATE", "DELETE" })
                        m.Sql($"CREATE TRIGGER \"TR_{table}_{action}_Immutable\" BEFORE {action} ON \"{table}\" BEGIN SELECT RAISE(ABORT,'Accepted history is immutable'); END;");
                m.Sql($"CREATE TRIGGER \"TR_ContributionOperations_Identity\" BEFORE INSERT ON \"ContributionOperations\" BEGIN SELECT RAISE(ABORT,'Draft item is Removed') WHERE EXISTS ({removed}); {sampleIndex} END;");
                m.Sql($"CREATE TRIGGER \"TR_ItemRemovalOperations_ScopeIdentity\" BEFORE INSERT ON \"ItemRemovalOperations\" BEGIN SELECT RAISE(ABORT,'Shared item scope mismatch') WHERE NOT EXISTS ({scope}); {itemIndex} END;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain Removed history and shared operation identities; use reviewed forward repair or verified restore.");
    }
}
