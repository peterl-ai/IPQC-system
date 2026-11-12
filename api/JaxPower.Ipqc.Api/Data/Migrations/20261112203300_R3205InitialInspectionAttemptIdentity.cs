using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205InitialInspectionAttemptIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PatrolTaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    CreationEventId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    CreationSource = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(40)" : "TEXT", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    CreatedByUsername = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(100)" : "TEXT", maxLength: 100, nullable: true),
                    CreatedByPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    InitializationReason = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(2000)" : "TEXT", maxLength: 2000, nullable: true),
                    PlanningSnapshotVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    FrozenSampleCount = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(4000)" : "TEXT", maxLength: 4000, nullable: false),
                    MinimumSampleCount = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionAttempts", x => x.Id);
                    table.UniqueConstraint("AK_InspectionAttempts_Id_PatrolTaskItemId", x => new { x.Id, x.PatrolTaskItemId });
                    table.CheckConstraint("CK_InspectionAttempts_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000' AND \"CreationEventId\" <> '00000000-0000-0000-0000-000000000000' AND \"Kind\" = 'Initial' AND \"PlanningSnapshotVersion\" = 1 AND \"MinimumSampleCount\" BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_InspectionAttempts_Provenance", "(\"CreationSource\" = 'TaskGeneration' AND \"CreatedByUserId\" IS NULL AND \"CreatedByUsername\" IS NULL AND \"CreatedByPersonalSessionId\" IS NULL AND \"InitializationReason\" IS NULL) OR (\"CreationSource\" = 'ExplicitInitialization' AND \"CreatedByUserId\" IS NOT NULL AND \"CreatedByUsername\" IS NOT NULL AND length(trim(\"CreatedByUsername\")) > 0 AND \"CreatedByPersonalSessionId\" IS NOT NULL AND \"CreatedByPersonalSessionId\" <> '00000000-0000-0000-0000-000000000000' AND \"InitializationReason\" IS NOT NULL AND length(trim(\"InitializationReason\")) BETWEEN 1 AND 2000)");
                    table.ForeignKey(
                        name: "FK_InspectionAttempts_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionAttempts_PatrolTaskItems_PatrolTaskItemId",
                        column: x => x.PatrolTaskItemId,
                        principalTable: "PatrolTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionAttemptApplicabilities",
                columns: table => new
                {
                    PatrolTaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    InspectionAttemptId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionAttemptApplicabilities", x => x.PatrolTaskItemId);
                    table.ForeignKey(
                        name: "FK_AttemptApplicabilities_AttemptScope",
                        columns: x => new { x.InspectionAttemptId, x.PatrolTaskItemId },
                        principalTable: "InspectionAttempts",
                        principalColumns: new[] { "Id", "PatrolTaskItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttemptApplicabilities_TaskItem",
                        column: x => x.PatrolTaskItemId,
                        principalTable: "PatrolTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttemptApplicabilities_InspectionAttemptId",
                table: "InspectionAttemptApplicabilities",
                column: "InspectionAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptApplicabilities_AttemptScope",
                table: "InspectionAttemptApplicabilities",
                columns: new[] { "InspectionAttemptId", "PatrolTaskItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttempts_CreatedByUserId",
                table: "InspectionAttempts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttempts_CreationEventId",
                table: "InspectionAttempts",
                column: "CreationEventId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionAttempts_PatrolTaskItemId",
                table: "InspectionAttempts",
                column: "PatrolTaskItemId",
                unique: true,
                filter: "\"Kind\" = 'Initial'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Retain inspection identity history; use reviewed forward repair or verified backup restoration.");
    }
}
