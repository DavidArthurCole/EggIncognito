using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyzedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analyzed_files",
                columns: table => new
                {
                    file_sha = table.Column<string>(type: "text", nullable: false),
                    first_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    source = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: true),
                    proto_sha = table.Column<string>(type: "text", nullable: true),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    build = table.Column<string>(type: "text", nullable: true),
                    client_version = table.Column<string>(type: "text", nullable: true),
                    file_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analyzed_files", x => x.file_sha);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analyzed_files_first_seen",
                table: "analyzed_files",
                column: "first_seen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analyzed_files");
        }
    }
}
