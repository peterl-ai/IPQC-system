using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3203LineSharedPlanningSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider.Contains("Npgsql");
            migrationBuilder.AddColumn<string>(
                name: "PlanningSnapshotJson",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanningSnapshotVersion",
                table: "PatrolTasks",
                type: postgres ? "integer" : "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SharedPlanRevision",
                table: "PatrolPlans",
                type: postgres ? "bigint" : "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_AdmittedLineOccurrence",
                table: "PatrolTasks",
                columns: new[] { "LineId", "ScheduledOccurrenceUtc" },
                unique: true,
                filter: "\"WorkflowVersion\" = 2 AND \"PlanningSnapshotVersion\" = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PatrolTasks_PlanningSnapshot",
                table: "PatrolTasks",
                sql: "(\"PlanningSnapshotVersion\" IS NULL AND \"PlanningSnapshotJson\" IS NULL) OR (\"WorkflowVersion\" = 2 AND \"PlanningSnapshotVersion\" IS NOT NULL AND \"PlanningSnapshotVersion\" = 1 AND \"PlanningSnapshotJson\" IS NOT NULL AND length(\"PlanningSnapshotJson\") > 0 AND \"AssignedInspectorUserId\" IS NULL AND \"AssignedInspectorKey\" = '')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PatrolPlans_SharedAdmission",
                table: "PatrolPlans",
                sql: "\"SharedPlanRevision\" IS NULL OR (\"WorkflowVersion\" = 2 AND \"SharedPlanRevision\" >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain frozen planning history; use reviewed forward repair or a verified backup.");
        }
    }
}
