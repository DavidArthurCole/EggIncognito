using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredApks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_apks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    package = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: false),
                    build = table.Column<string>(type: "text", nullable: false),
                    split = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    source_device_id = table.Column<string>(type: "text", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_apks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_apks_platform_package_app_version_build_split",
                table: "stored_apks",
                columns: new[] { "platform", "package", "app_version", "build", "split" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stored_apks_platform_package_build",
                table: "stored_apks",
                columns: new[] { "platform", "package", "build" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_apks_sha256",
                table: "stored_apks",
                column: "sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_apks");
        }
    }
}
