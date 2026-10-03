using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPatrolStandards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            migrationBuilder.CreateTable(
                name: "PatrolStandards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    StandardNo = table.Column<string>(maxLength: 50, nullable: true),
                    PatrolStandardName = table.Column<string>(maxLength: 200, nullable: false),
                    FactoryCode = table.Column<string>(maxLength: 100, nullable: false),
                    FactoryName = table.Column<string>(maxLength: 200, nullable: false),
                    WorkshopCode = table.Column<string>(maxLength: 100, nullable: false),
                    LineCode = table.Column<string>(maxLength: 100, nullable: false),
                    LineName = table.Column<string>(maxLength: 200, nullable: false),
                    MaterialCode = table.Column<string>(maxLength: 100, nullable: false),
                    CreatedBy = table.Column<string>(maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: postgres ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolStandards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatrolStandardItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    PatrolStandardId = table.Column<Guid>(type: postgres ? "uuid" : "TEXT", nullable: false),
                    SequenceNo = table.Column<int>(nullable: false),
                    ProcessCode = table.Column<string>(maxLength: 2000, nullable: false),
                    ProcessName = table.Column<string>(maxLength: 2000, nullable: false),
                    InspectionItemCategory = table.Column<string>(maxLength: 2000, nullable: false),
                    InspectionItem = table.Column<string>(maxLength: 2000, nullable: false),
                    InspectionContent = table.Column<string>(maxLength: 2000, nullable: false),
                    UpperLimitOperator = table.Column<string>(maxLength: 2000, nullable: false),
                    UpperLimitValue = table.Column<string>(maxLength: 2000, nullable: false),
                    LowerLimitOperator = table.Column<string>(maxLength: 2000, nullable: false),
                    LowerLimitValue = table.Column<string>(maxLength: 2000, nullable: false),
                    InspectionType = table.Column<string>(maxLength: 2000, nullable: false),
                    SamplingPlan = table.Column<string>(maxLength: 2000, nullable: false),
                    SampleCount = table.Column<string>(maxLength: 2000, nullable: false),
                    PhotoRequirement = table.Column<string>(maxLength: 2000, nullable: false),
                    DefectLevel = table.Column<string>(maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatrolStandardItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatrolStandardItems_PatrolStandards_PatrolStandardId",
                        column: x => x.PatrolStandardId,
                        principalTable: "PatrolStandards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolStandardItems_PatrolStandardId_SequenceNo",
                table: "PatrolStandardItems",
                columns: new[] { "PatrolStandardId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolStandards_FactoryCode",
                table: "PatrolStandards",
                column: "FactoryCode");

            migrationBuilder.CreateIndex(
                name: "IX_PatrolStandards_LineCode",
                table: "PatrolStandards",
                column: "LineCode");

            migrationBuilder.CreateIndex(
                name: "IX_PatrolStandards_PatrolStandardName",
                table: "PatrolStandards",
                column: "PatrolStandardName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatrolStandardItems");

            migrationBuilder.DropTable(
                name: "PatrolStandards");
        }
    }
}
