using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_updates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    from_version = table.Column<string>(type: "text", nullable: true),
                    to_version = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    triggered_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_updates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_updates_device_id_attempted_at",
                table: "device_updates",
                columns: new[] { "device_id", "attempted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_updates");
        }
    }
}
