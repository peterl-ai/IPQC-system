using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PatrolPlansAndScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            migrationBuilder.CreateTable(
                name: "PatrolPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PlanNo = table.Column<string>(type: postgres ? "character varying(50)" : "TEXT", maxLength: 50, nullable: false),
                    PlanName = table.Column<string>(type: postgres ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    PatrolStandardId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    FactoryCode = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    FactoryName = table.Column<string>(type: postgres ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    LineCode = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    LineName = table.Column<string>(type: postgres ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: postgres ? "boolean" : "INTEGER", nullable: false),
                    EffectiveStartUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    EffectiveEndUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: true),
                    ScheduleDefinition = table.Column<string>(nullable: false),
                    TimeZoneId = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    GenerationNotBeforeUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolPlans_PatrolStandards_PatrolStandardId",
                        column: x => x.PatrolStandardId,
                        principalTable: "PatrolStandards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatrolPlanAssignees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolPlanId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    AssigneeKey = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: postgres ? "boolean" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolPlanAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolPlanAssignees_PatrolPlans_PatrolPlanId",
                        column: x => x.PatrolPlanId,
                        principalTable: "PatrolPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatrolTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    TaskNo = table.Column<string>(type: postgres ? "character varying(50)" : "TEXT", maxLength: 50, nullable: false),
                    PatrolPlanId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolStandardId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    AssignedInspectorKey = table.Column<string>(type: postgres ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    ScheduledOccurrenceUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    GenerationSource = table.Column<string>(type: postgres ? "character varying(50)" : "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: postgres ? "character varying(50)" : "TEXT", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolTasks_PatrolPlans_PatrolPlanId",
                        column: x => x.PatrolPlanId,
                        principalTable: "PatrolPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatrolTasks_PatrolStandards_PatrolStandardId",
                        column: x => x.PatrolStandardId,
                        principalTable: "PatrolStandards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlanAssignees_PatrolPlanId_AssigneeKey",
                table: "PatrolPlanAssignees",
                columns: new[] { "PatrolPlanId", "AssigneeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlans_IsEnabled_EffectiveStartUtc_EffectiveEndUtc",
                table: "PatrolPlans",
                columns: new[] { "IsEnabled", "EffectiveStartUtc", "EffectiveEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlans_PatrolStandardId",
                table: "PatrolPlans",
                column: "PatrolStandardId");

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlans_PlanNo",
                table: "PatrolPlans",
                column: "PlanNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_PatrolPlanId_ScheduledOccurrenceUtc",
                table: "PatrolTasks",
                columns: new[] { "PatrolPlanId", "ScheduledOccurrenceUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_PatrolStandardId",
                table: "PatrolTasks",
                column: "PatrolStandardId");

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_TaskNo",
                table: "PatrolTasks",
                column: "TaskNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatrolPlanAssignees");

            migrationBuilder.DropTable(
                name: "PatrolTasks");

            migrationBuilder.DropTable(
                name: "PatrolPlans");
        }
    }
}
