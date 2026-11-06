using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class P101WorkflowLineFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guid = postgres ? "uuid" : "TEXT";
            var integer = postgres ? "integer" : "INTEGER";
            var boolean = postgres ? "boolean" : "INTEGER";
            migrationBuilder.AddColumn<Guid>(
                name: "LineId",
                table: "PatrolTasks",
                type: guid,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowVersion",
                table: "PatrolTasks",
                type: integer,
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "LineId",
                table: "PatrolPlans",
                type: guid,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowVersion",
                table: "PatrolPlans",
                type: integer,
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ProductionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: boolean, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLines", x => x.Id);
                    table.CheckConstraint("CK_ProductionLines_Code", "length(trim(\"Code\")) > 0 AND length(trim(\"NormalizedCode\")) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_LineId",
                table: "PatrolTasks",
                column: "LineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PatrolTasks_Workflow",
                table: "PatrolTasks",
                sql: "\"WorkflowVersion\" IN (1, 2) AND (\"WorkflowVersion\" = 1 OR \"LineId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlans_LineId",
                table: "PatrolPlans",
                column: "LineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PatrolPlans_Workflow",
                table: "PatrolPlans",
                sql: "\"WorkflowVersion\" IN (1, 2) AND (\"WorkflowVersion\" = 1 OR \"LineId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLines_NormalizedCode",
                table: "ProductionLines",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PatrolPlans_ProductionLines_LineId",
                table: "PatrolPlans",
                column: "LineId",
                principalTable: "ProductionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatrolTasks_ProductionLines_LineId",
                table: "PatrolTasks",
                column: "LineId",
                principalTable: "ProductionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PatrolPlans_ProductionLines_LineId",
                table: "PatrolPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_PatrolTasks_ProductionLines_LineId",
                table: "PatrolTasks");

            migrationBuilder.DropTable(
                name: "ProductionLines");

            migrationBuilder.DropIndex(
                name: "IX_PatrolTasks_LineId",
                table: "PatrolTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PatrolTasks_Workflow",
                table: "PatrolTasks");

            migrationBuilder.DropIndex(
                name: "IX_PatrolPlans_LineId",
                table: "PatrolPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PatrolPlans_Workflow",
                table: "PatrolPlans");

            migrationBuilder.DropColumn(
                name: "LineId",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "LineId",
                table: "PatrolPlans");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                table: "PatrolPlans");
        }
    }
}
