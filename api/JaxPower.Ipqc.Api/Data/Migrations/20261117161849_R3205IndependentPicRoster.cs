using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205IndependentPicRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PicRosterEntries",
                columns: table => new
                {
                    PicId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PicRosterEntries", x => x.PicId);
                    table.CheckConstraint("CK_Pic_Identity", "\"PicId\"<>'00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "PicRosterRevisions",
                columns: table => new
                {
                    PicId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Revision = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    RequestId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ContractVersion = table.Column<int>(type: ActiveProvider.Contains("Npgsql") ? "integer" : "INTEGER", nullable: false),
                    RequestHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(16)" : "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: ActiveProvider.Contains("Npgsql") ? "boolean" : "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorUsername = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(100)" : "TEXT", maxLength: 100, nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PicRosterRevisions", x => new { x.PicId, x.Revision });
                    table.CheckConstraint("CK_Pic_Revision", "\"Revision\">0 AND \"ContractVersion\"=1 AND \"RequestId\"<>'00000000-0000-0000-0000-000000000000' AND length(\"RequestHash\")=64 AND length(\"Name\") BETWEEN 1 AND 200 AND length(trim(\"Name\"))>0 AND length(\"Department\") BETWEEN 1 AND 200 AND length(trim(\"Department\"))>0 AND length(\"ActorUsername\") BETWEEN 1 AND 100 AND length(\"EnvironmentId\") BETWEEN 1 AND 200 AND \"ActorPersonalSessionId\"<>'00000000-0000-0000-0000-000000000000' AND \"Action\" IN ('Created','Updated','Activated','Deactivated')");
                    table.ForeignKey(
                        name: "FK_PicRosterRevisions_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PicRosterRevisions_PicRosterEntries_PicId",
                        column: x => x.PicId,
                        principalTable: "PicRosterEntries",
                        principalColumn: "PicId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PicRosterRevisions_ActorUserId",
                table: "PicRosterRevisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PicRosterRevisions_RequestId",
                table: "PicRosterRevisions",
                column: "RequestId",
                unique: true);
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("PIC identity and audit history cannot be downgraded.");
    }
}
