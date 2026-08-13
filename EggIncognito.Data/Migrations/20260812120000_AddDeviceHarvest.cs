using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceHarvest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_state",
                columns: table => new
                {
                    device_id = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    package = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    build = table.Column<string>(type: "text", nullable: true),
                    client_version = table.Column<int>(type: "integer", nullable: true),
                    revision = table.Column<string>(type: "text", nullable: false),
                    harvested_revision = table.Column<string>(type: "text", nullable: true),
                    dirty = table.Column<bool>(type: "boolean", nullable: false),
                    harvesting = table.Column<bool>(type: "boolean", nullable: false),
                    last_harvest_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_harvest_status = table.Column<string>(type: "text", nullable: false),
                    last_harvest_note = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_state", x => x.device_id);
                });

            migrationBuilder.CreateTable(
                name: "device_assets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    source_version = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_harvest_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    ran_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    revision = table.Column<string>(type: "text", nullable: false),
                    entry = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_harvest_log", x => x.id);
                });

            migrationBuilder.AddColumn<string>(
                name: "input_sha",
                table: "game_data_documents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_assets_platform_kind_name",
                table: "device_assets",
                columns: new[] { "platform", "kind", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_assets_sha256",
                table: "device_assets",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "IX_device_harvest_log_device_id_ran_at",
                table: "device_harvest_log",
                columns: new[] { "device_id", "ran_at" });

            migrationBuilder.Sql(
                "INSERT INTO device_assets (platform, kind, name, sha256, bytes, byte_size, content_type, updated_at) " +
                "SELECT 'any', 'icon', name, encode(sha256(bytes), 'hex'), bytes, byte_size, content_type, created_at " +
                "FROM stored_icons ON CONFLICT DO NOTHING");

            migrationBuilder.DropTable(name: "stored_meshes");
            migrationBuilder.DropTable(name: "stored_icons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_meshes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    stem = table.Column<string>(type: "text", nullable: false),
                    glb = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_meshes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_meshes_platform_stem",
                table: "stored_meshes",
                columns: new[] { "platform", "stem" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "stored_icons",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    provenance = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_icons", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_icons_name",
                table: "stored_icons",
                column: "name",
                unique: true);

            migrationBuilder.DropColumn(name: "input_sha", table: "game_data_documents");

            migrationBuilder.DropTable(name: "device_harvest_log");
            migrationBuilder.DropTable(name: "device_assets");
            migrationBuilder.DropTable(name: "device_state");
        }
    }
}
