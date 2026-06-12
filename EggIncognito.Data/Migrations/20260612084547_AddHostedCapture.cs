using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capture_proxy_tokens",
                columns: table => new
                {
                    discord_id = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_proxy_tokens", x => x.discord_id);
                });

            migrationBuilder.CreateTable(
                name: "capture_user_cas",
                columns: table => new
                {
                    discord_id = table.Column<string>(type: "text", nullable: false),
                    pfx = table.Column<byte[]>(type: "bytea", nullable: false),
                    thumbprint = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_user_cas", x => x.discord_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_proxy_tokens");

            migrationBuilder.DropTable(
                name: "capture_user_cas");
        }
    }
}
