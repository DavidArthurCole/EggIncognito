using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisionedInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provisioned_instances",
                columns: table => new
                {
                    instance_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    image = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    adb_serial = table.Column<string>(type: "text", nullable: true),
                    host_ref = table.Column<string>(type: "text", nullable: true),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioned_instances", x => x.instance_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provisioned_instances_device_id",
                table: "provisioned_instances",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_provisioned_instances_state",
                table: "provisioned_instances",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioned_instances");
        }
    }
}
