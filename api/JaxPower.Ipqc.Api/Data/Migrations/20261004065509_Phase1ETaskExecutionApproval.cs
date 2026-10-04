using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1ETaskExecutionApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "PatrolTasks",
                type: postgres ? "timestamp with time zone" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentRevisionNo",
                table: "PatrolTasks",
                type: postgres ? "integer" : "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FactoryCodeSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactoryNameSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineCodeSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineNameSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaterialCodeSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverallInspectionResult",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanNameSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanNoSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceStandardUpdatedAtUtc",
                table: "PatrolTasks",
                type: postgres ? "timestamp with time zone" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandardNameSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "PatrolTasks",
                type: postgres ? "timestamp with time zone" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "PatrolTasks",
                type: postgres ? "timestamp with time zone" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkshopCodeSnapshot",
                table: "PatrolTasks",
                type: postgres ? "text" : "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatrolTaskItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SourcePatrolStandardItemId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SequenceNo = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    ProcessCode = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    ProcessName = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    InspectionItemCategory = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    InspectionItem = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    InspectionContent = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    UpperLimitOperator = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    UpperLimitValue = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    LowerLimitOperator = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    LowerLimitValue = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    InspectionType = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    SamplingPlan = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    SampleCount = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    PhotoRequirement = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    DefectLevel = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    IsNa = table.Column<bool>(type: postgres ? "boolean" : "INTEGER", nullable: true),
                    JudgmentResult = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    MachineCode = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    Series = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    Mold = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    AbnormalType = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    AbnormalCause = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    Remarks = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTaskItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTaskItems_PatrolTasks_PatrolTaskId",
                        column: x => x.PatrolTaskId,
                        principalTable: "PatrolTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatrolTaskReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    RevisionNo = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    Reviewer = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 100, nullable: false),
                    Decision = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTaskReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTaskReviews_PatrolTasks_PatrolTaskId",
                        column: x => x.PatrolTaskId,
                        principalTable: "PatrolTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatrolTaskSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    RevisionNo = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    Shift = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 10, nullable: false),
                    OverallInspectionResult = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 20, nullable: false),
                    SubmittedBy = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 100, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTaskSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTaskSubmissions_PatrolTasks_PatrolTaskId",
                        column: x => x.PatrolTaskId,
                        principalTable: "PatrolTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatrolTaskItemSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskItemId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SequenceNo = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    InspectionValue = table.Column<decimal>(type: postgres ? "numeric" : "TEXT", nullable: true),
                    JudgmentResult = table.Column<string>(type: postgres ? "text" : "TEXT", nullable: true),
                    InspectedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTaskItemSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTaskItemSamples_PatrolTaskItems_PatrolTaskItemId",
                        column: x => x.PatrolTaskItemId,
                        principalTable: "PatrolTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatrolTaskSubmissionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskSubmissionId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskItemId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SequenceNo = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    IsNa = table.Column<bool>(type: postgres ? "boolean" : "INTEGER", nullable: false),
                    JudgmentResult = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: false),
                    MachineCode = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    Series = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    Mold = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    AbnormalType = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    AbnormalCause = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true),
                    Remarks = table.Column<string>(type: postgres ? "text" : "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTaskSubmissionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTaskSubmissionItems_PatrolTaskSubmissions_PatrolTaskSubmissionId",
                        column: x => x.PatrolTaskSubmissionId,
                        principalTable: "PatrolTaskSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatrolTaskSubmissionSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskSubmissionItemId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SequenceNo = table.Column<int>(type: postgres ? "integer" : "INTEGER", nullable: false),
                    InspectionValue = table.Column<decimal>(type: postgres ? "numeric" : "TEXT", nullable: true),
                    JudgmentResult = table.Column<string>(type: postgres ? "text" : "TEXT", nullable: false),
                    InspectedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTaskSubmissionSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTaskSubmissionSamples_PatrolTaskSubmissionItems_PatrolTaskSubmissionItemId",
                        column: x => x.PatrolTaskSubmissionItemId,
                        principalTable: "PatrolTaskSubmissionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_Status_ScheduledOccurrenceUtc",
                table: "PatrolTasks",
                columns: new[] { "Status", "ScheduledOccurrenceUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTaskItems_PatrolTaskId_SequenceNo",
                table: "PatrolTaskItems",
                columns: new[] { "PatrolTaskId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTaskItemSamples_PatrolTaskItemId_SequenceNo",
                table: "PatrolTaskItemSamples",
                columns: new[] { "PatrolTaskItemId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTaskReviews_PatrolTaskId_RevisionNo",
                table: "PatrolTaskReviews",
                columns: new[] { "PatrolTaskId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTaskSubmissionItems_PatrolTaskSubmissionId_SequenceNo",
                table: "PatrolTaskSubmissionItems",
                columns: new[] { "PatrolTaskSubmissionId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTaskSubmissions_PatrolTaskId_RevisionNo",
                table: "PatrolTaskSubmissions",
                columns: new[] { "PatrolTaskId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTaskSubmissionSamples_PatrolTaskSubmissionItemId_SequenceNo",
                table: "PatrolTaskSubmissionSamples",
                columns: new[] { "PatrolTaskSubmissionItemId", "SequenceNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatrolTaskItemSamples");

            migrationBuilder.DropTable(
                name: "PatrolTaskReviews");

            migrationBuilder.DropTable(
                name: "PatrolTaskSubmissionSamples");

            migrationBuilder.DropTable(
                name: "PatrolTaskItems");

            migrationBuilder.DropTable(
                name: "PatrolTaskSubmissionItems");

            migrationBuilder.DropTable(
                name: "PatrolTaskSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_PatrolTasks_Status_ScheduledOccurrenceUtc",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "CurrentRevisionNo",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "FactoryCodeSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "FactoryNameSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "LineCodeSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "LineNameSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "MaterialCodeSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "OverallInspectionResult",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "PlanNameSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "PlanNoSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "Shift",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "SourceStandardUpdatedAtUtc",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "StandardNameSnapshot",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "WorkshopCodeSnapshot",
                table: "PatrolTasks");
        }
    }
}
