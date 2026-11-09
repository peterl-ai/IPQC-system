using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JaxPower.Ipqc.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class P102DeviceRegistryProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guid = postgres ? "uuid" : "TEXT";
            var integer = postgres ? "integer" : "INTEGER";
            var big = postgres ? "bigint" : "INTEGER";
            var date = postgres ? "timestamp with time zone" : "TEXT";
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "ProductionLines",
                type: big,
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "RegisteredDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Serial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: integer, nullable: false),
                    LineId = table.Column<Guid>(type: guid, nullable: true),
                    Revision = table.Column<long>(type: big, nullable: false),
                    BindingVersion = table.Column<long>(type: big, nullable: false),
                    ReplacedByDeviceId = table.Column<Guid>(type: guid, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: guid, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: guid, nullable: false),
                    TerminalAtUtc = table.Column<DateTime>(type: date, nullable: true),
                    TerminalReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredDevices", x => x.Id);
                    table.CheckConstraint("CK_RegisteredDevices_Provisioning", "\"Status\" IN (1, 2, 3) AND \"Revision\" >= 1 AND \"BindingVersion\" >= 1 AND ((\"Status\" = 1 AND \"TerminalAtUtc\" IS NULL AND \"TerminalReason\" IS NULL) OR (\"Status\" IN (2, 3) AND \"LineId\" IS NULL AND \"TerminalAtUtc\" IS NOT NULL AND \"TerminalReason\" IS NOT NULL AND length(trim(\"TerminalReason\")) > 0)) AND ((\"Status\" = 3 AND \"ReplacedByDeviceId\" IS NOT NULL AND \"ReplacedByDeviceId\" <> \"Id\") OR (\"Status\" <> 3 AND \"ReplacedByDeviceId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_RegisteredDevices_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegisteredDevices_ApplicationUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegisteredDevices_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegisteredDevices_RegisteredDevices_ReplacedByDeviceId",
                        column: x => x.ReplacedByDeviceId,
                        principalTable: "RegisteredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: guid, nullable: false),
                    ChangeId = table.Column<Guid>(type: guid, nullable: false),
                    ProductionLineId = table.Column<Guid>(type: guid, nullable: true),
                    DeviceId = table.Column<Guid>(type: guid, nullable: true),
                    OldLineId = table.Column<Guid>(type: guid, nullable: true),
                    NewLineId = table.Column<Guid>(type: guid, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ActorUserId = table.Column<Guid>(type: guid, nullable: false),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: date, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_ProvisioningAuditEvents_Aggregate", "(\"DeviceId\" IS NOT NULL AND \"ProductionLineId\" IS NULL) OR (\"DeviceId\" IS NULL AND \"ProductionLineId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_ProductionLines_NewLineId",
                        column: x => x.NewLineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_ProductionLines_OldLineId",
                        column: x => x.OldLineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_ProductionLines_ProductionLineId",
                        column: x => x.ProductionLineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningAuditEvents_RegisteredDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "RegisteredDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionLines_Revision",
                table: "ProductionLines",
                sql: "\"Revision\" >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_ActorUserId",
                table: "ProvisioningAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_ChangeId",
                table: "ProvisioningAuditEvents",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_DeviceId_OccurredAtUtc",
                table: "ProvisioningAuditEvents",
                columns: new[] { "DeviceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_NewLineId",
                table: "ProvisioningAuditEvents",
                column: "NewLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_OldLineId",
                table: "ProvisioningAuditEvents",
                column: "OldLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningAuditEvents_ProductionLineId_OccurredAtUtc",
                table: "ProvisioningAuditEvents",
                columns: new[] { "ProductionLineId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDevices_CreatedByUserId",
                table: "RegisteredDevices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDevices_LineId",
                table: "RegisteredDevices",
                column: "LineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDevices_ReplacedByDeviceId",
                table: "RegisteredDevices",
                column: "ReplacedByDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDevices_UpdatedByUserId",
                table: "RegisteredDevices",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProvisioningAuditEvents");

            migrationBuilder.DropTable(
                name: "RegisteredDevices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionLines_Revision",
                table: "ProductionLines");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ProductionLines");
        }
    }
}
