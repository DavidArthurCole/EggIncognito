using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_endpoints",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    path = table.Column<string>(type: "text", nullable: false),
                    eid = table.Column<string>(type: "text", nullable: true),
                    response_json = table.Column<string>(type: "text", nullable: false),
                    response_type = table.Column<string>(type: "text", nullable: false),
                    owner_user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_endpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_routes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    path = table.Column<string>(type: "text", nullable: false),
                    request_type = table.Column<string>(type: "text", nullable: true),
                    response_type = table.Column<string>(type: "text", nullable: true),
                    request_wrapped = table.Column<bool>(type: "boolean", nullable: false),
                    response_wrapped = table.Column<bool>(type: "boolean", nullable: false),
                    raw_response = table.Column<string>(type: "text", nullable: true),
                    path_param = table.Column<bool>(type: "boolean", nullable: false),
                    path_param_only = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    owner_user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_routes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_endpoints_path_eid",
                table: "stored_endpoints",
                columns: new[] { "path", "eid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stored_routes_path",
                table: "stored_routes",
                column: "path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_endpoints");

            migrationBuilder.DropTable(
                name: "stored_routes");
        }
    }
}
