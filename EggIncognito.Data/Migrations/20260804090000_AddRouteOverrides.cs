using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_overrides",
                columns: table => new
                {
                    path = table.Column<string>(type: "text", nullable: false),
                    request_type = table.Column<string>(type: "text", nullable: true),
                    response_type = table.Column<string>(type: "text", nullable: true),
                    request_wrapped = table.Column<bool>(type: "boolean", nullable: true),
                    response_wrapped = table.Column<bool>(type: "boolean", nullable: true),
                    path_param = table.Column<bool>(type: "boolean", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_overrides", x => x.path);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_overrides");
        }
    }
}
