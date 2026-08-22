using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSymbolizedBinariesAndRoutePlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "platform",
                table: "route_binary_catalog",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "symbolized_binaries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    symbol_count = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symbolized_binaries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_symbolized_binaries_platform_app_version",
                table: "symbolized_binaries",
                columns: new[] { "platform", "app_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_symbolized_binaries_sha256",
                table: "symbolized_binaries",
                column: "sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "symbolized_binaries");

            migrationBuilder.DropColumn(
                name: "platform",
                table: "route_binary_catalog");
        }
    }
}
