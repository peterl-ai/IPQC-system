using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1GLocalAuthenticationUsersRbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider.Contains("Npgsql", StringComparison.Ordinal);
            var guid = postgres ? "uuid" : "TEXT";
            var text = "TEXT";
            var date = postgres ? "timestamp with time zone" : "TEXT";
            var integer = postgres ? "integer" : "INTEGER";
            var boolean = postgres ? "boolean" : "INTEGER";
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedInspectorUserId",
                table: "PatrolTasks",
                type: guid,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "PatrolPlanAssignees",
                type: guid,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    Username = table.Column<string>(type: text, maxLength: 100, nullable: false),
                    NormalizedUsername = table.Column<string>(type: text, maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: text, maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: text, maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: text, maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: boolean, nullable: false),
                    MustChangePassword = table.Column<bool>(type: boolean, nullable: false),
                    AuthVersion = table.Column<int>(type: integer, nullable: false),
                    FailedLoginCount = table.Column<int>(type: integer, nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: date, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    CreatedBy = table.Column<string>(type: text, maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    UpdatedBy = table.Column<string>(type: text, maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokenSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    UserId = table.Column<Guid>(type: guid, nullable: false),
                    TokenHash = table.Column<string>(type: text, maxLength: 64, nullable: false),
                    AuthVersion = table.Column<int>(type: integer, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    ReplacedBySessionId = table.Column<Guid>(type: guid, nullable: true),
                    UserAgent = table.Column<string>(type: text, maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokenSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokenSessions_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatrolTasks_AssignedInspectorUserId",
                table: "PatrolTasks",
                column: "AssignedInspectorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlanAssignees_PatrolPlanId_UserId",
                table: "PatrolPlanAssignees",
                columns: new[] { "PatrolPlanId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatrolPlanAssignees_UserId",
                table: "PatrolPlanAssignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_NormalizedUsername",
                table: "ApplicationUsers",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenSessions_TokenHash",
                table: "RefreshTokenSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokenSessions_UserId_RevokedAtUtc",
                table: "RefreshTokenSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_PatrolPlanAssignees_ApplicationUsers_UserId",
                table: "PatrolPlanAssignees",
                column: "UserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatrolTasks_ApplicationUsers_AssignedInspectorUserId",
                table: "PatrolTasks",
                column: "AssignedInspectorUserId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PatrolPlanAssignees_ApplicationUsers_UserId",
                table: "PatrolPlanAssignees");

            migrationBuilder.DropForeignKey(
                name: "FK_PatrolTasks_ApplicationUsers_AssignedInspectorUserId",
                table: "PatrolTasks");

            migrationBuilder.DropTable(
                name: "RefreshTokenSessions");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_PatrolTasks_AssignedInspectorUserId",
                table: "PatrolTasks");

            migrationBuilder.DropIndex(
                name: "IX_PatrolPlanAssignees_PatrolPlanId_UserId",
                table: "PatrolPlanAssignees");

            migrationBuilder.DropIndex(
                name: "IX_PatrolPlanAssignees_UserId",
                table: "PatrolPlanAssignees");

            migrationBuilder.DropColumn(
                name: "AssignedInspectorUserId",
                table: "PatrolTasks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PatrolPlanAssignees");
        }
    }
}
