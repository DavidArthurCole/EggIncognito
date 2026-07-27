using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameDataDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_data_documents",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    json = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_data_documents", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_data_documents");
        }
    }
}
