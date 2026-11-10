using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3202PersonalSessionReadFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider.Contains("Npgsql");
            migrationBuilder.AddColumn<string>(
                name: "PersonalSessionEnvironmentId",
                table: "RefreshTokenSessions",
                type: postgres ? "character varying(200)" : "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonalSessionId",
                table: "RefreshTokenSessions",
                type: postgres ? "uuid" : "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenSessions_PersonalSessionId",
                table: "RefreshTokenSessions",
                column: "PersonalSessionId",
                unique: true,
                filter: "\"PersonalSessionId\" IS NOT NULL AND \"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenSessions_UserId_PersonalSessionId",
                table: "RefreshTokenSessions",
                columns: new[] { "UserId", "PersonalSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_WorkflowVersion_LineId_ScheduledOccurrenceUtc_Id",
                table: "PatrolTasks",
                columns: new[] { "WorkflowVersion", "LineId", "ScheduledOccurrenceUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain personal-session history; use a reviewed forward repair or verified backup.");
        }
    }
}
