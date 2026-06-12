using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class TuneIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subject_tags_subject_kind_subject_key",
                table: "subject_tags");

            migrationBuilder.CreateIndex(
                name: "IX_stored_routes_source",
                table: "stored_routes",
                column: "source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stored_routes_source",
                table: "stored_routes");

            migrationBuilder.CreateIndex(
                name: "IX_subject_tags_subject_kind_subject_key",
                table: "subject_tags",
                columns: new[] { "subject_kind", "subject_key" });
        }
    }
}
