using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProtoShaOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proto_sha_orders",
                columns: table => new
                {
                    proto_sha = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proto_sha_orders", x => x.proto_sha);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proto_sha_orders");
        }
    }
}
