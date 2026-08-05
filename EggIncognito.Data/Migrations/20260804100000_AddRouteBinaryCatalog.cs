using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteBinaryCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_binary_catalog",
                columns: table => new
                {
                    path = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: true),
                    request_type = table.Column<string>(type: "text", nullable: true),
                    response_type = table.Column<string>(type: "text", nullable: true),
                    request_wrapped = table.Column<bool>(type: "boolean", nullable: false),
                    response_wrapped = table.Column<bool>(type: "boolean", nullable: false),
                    binary_version = table.Column<string>(type: "text", nullable: true),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_binary_catalog", x => x.path);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_binary_catalog");
        }
    }
}
