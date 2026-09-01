using System;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260831120000_AddProtoCanonicals")]
    public partial class AddProtoCanonicals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proto_canonicals",
                columns: table => new
                {
                    proto_sha = table.Column<string>(type: "text", nullable: false),
                    canonical_sha = table.Column<string>(type: "text", nullable: true),
                    canonical_text = table.Column<string>(type: "text", nullable: true),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proto_canonicals", x => x.proto_sha);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proto_canonicals");
        }
    }
}
