using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class R3205ConfirmedPhotoPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoPolicyDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StandardId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StandardItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    Version = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    DefinitionHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    DefinitionJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoPolicyDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_PhotoPolicyDefinition_Item", x => new { x.Id, x.StandardItemId });
                    table.CheckConstraint("CK_PhotoPolicyDefinition", "\"Version\">0 AND length(\"DefinitionHash\")=64 AND length(\"DefinitionJson\")>0");
                });

            migrationBuilder.CreateTable(
                name: "PhotoPolicyConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StandardId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StandardItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    PolicyVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    ExpectedConfirmationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    Value = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(16)" : "TEXT", maxLength: 16, nullable: false),
                    ActorUserId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ActorPersonalSessionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    EnvironmentId = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(200)" : "TEXT", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false),
                    ReceiptJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: ActiveProvider.Contains("Npgsql") ? "timestamp with time zone" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoPolicyConfirmations", x => x.Id);
                    table.UniqueConstraint("AK_PhotoPolicyConfirmation_State", x => new { x.Id, x.DefinitionId, x.StandardItemId, x.PolicyVersion });
                    table.CheckConstraint("CK_PhotoPolicyConfirmation", "\"Value\" IN ('Required','Optional') AND \"PolicyVersion\">0 AND length(\"RequestHash\")=64 AND length(\"ReceiptJson\")>0 AND length(\"EnvironmentId\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_PhotoPolicyConfirmation_Definition",
                        columns: x => new { x.DefinitionId, x.StandardItemId },
                        principalTable: "PhotoPolicyDefinitions",
                        principalColumns: new[] { "Id", "StandardItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhotoPolicyConfirmations_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FrozenTaskPhotoPolicies",
                columns: table => new
                {
                    TaskItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ConfirmationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "text" : "TEXT", nullable: false),
                    SnapshotHash = table.Column<string>(type: ActiveProvider.Contains("Npgsql") ? "character varying(64)" : "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenTaskPhotoPolicies", x => x.TaskItemId);
                    table.CheckConstraint("CK_FrozenTaskPhotoPolicy", "length(\"SnapshotHash\")=64 AND length(\"SnapshotJson\")>0");
                    table.ForeignKey(
                        name: "FK_FrozenTaskPhotoPolicies_PatrolTaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "PatrolTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenPhotoPolicy_Confirmation",
                        column: x => x.ConfirmationId,
                        principalTable: "PhotoPolicyConfirmations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoPolicyStates",
                columns: table => new
                {
                    StandardItemId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    StandardId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: false),
                    ConfirmationId = table.Column<Guid>(type: ActiveProvider.Contains("Npgsql") ? "uuid" : "TEXT", nullable: true),
                    PolicyVersion = table.Column<long>(type: ActiveProvider.Contains("Npgsql") ? "bigint" : "INTEGER", nullable: false),
                    IsPresent = table.Column<bool>(type: ActiveProvider.Contains("Npgsql") ? "boolean" : "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoPolicyStates", x => x.StandardItemId);
                    table.CheckConstraint("CK_PhotoPolicyState", "\"PolicyVersion\">=0 AND (\"IsPresent\" OR \"ConfirmationId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PhotoPolicyState_Confirmation",
                        columns: x => new { x.ConfirmationId, x.DefinitionId, x.StandardItemId, x.PolicyVersion },
                        principalTable: "PhotoPolicyConfirmations",
                        principalColumns: new[] { "Id", "DefinitionId", "StandardItemId", "PolicyVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhotoPolicyState_Definition",
                        columns: x => new { x.DefinitionId, x.StandardItemId },
                        principalTable: "PhotoPolicyDefinitions",
                        principalColumns: new[] { "Id", "StandardItemId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenTaskPhotoPolicies_ConfirmationId",
                table: "FrozenTaskPhotoPolicies",
                column: "ConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenTaskPhotoPolicies_TaskId",
                table: "FrozenTaskPhotoPolicies",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyConfirmation_Version",
                table: "PhotoPolicyConfirmations",
                columns: new[] { "StandardItemId", "PolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyConfirmations_ActorUserId",
                table: "PhotoPolicyConfirmations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyConfirmations_DefinitionId_StandardItemId",
                table: "PhotoPolicyConfirmations",
                columns: new[] { "DefinitionId", "StandardItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyConfirmations_RequestId",
                table: "PhotoPolicyConfirmations",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyDefinition_Version",
                table: "PhotoPolicyDefinitions",
                columns: new[] { "StandardItemId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyState_Confirmation",
                table: "PhotoPolicyStates",
                columns: new[] { "ConfirmationId", "DefinitionId", "StandardItemId", "PolicyVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoPolicyStates_DefinitionId_StandardItemId",
                table: "PhotoPolicyStates",
                columns: new[] { "DefinitionId", "StandardItemId" });
            Protect(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Photo policy confirmation and frozen history cannot be downgraded.");
    }
}
