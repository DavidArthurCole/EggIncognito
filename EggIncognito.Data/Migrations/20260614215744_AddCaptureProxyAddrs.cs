using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureProxyAddrs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_proxy_tokens");

            migrationBuilder.CreateTable(
                name: "capture_proxy_addrs",
                columns: table => new
                {
                    discord_id = table.Column<string>(type: "text", nullable: false),
                    addr = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_proxy_addrs", x => x.discord_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_capture_proxy_addrs_addr",
                table: "capture_proxy_addrs",
                column: "addr",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_proxy_addrs");

            migrationBuilder.CreateTable(
                name: "capture_proxy_tokens",
                columns: table => new
                {
                    discord_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    token_hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_proxy_tokens", x => x.discord_id);
                });
        }
    }
}
