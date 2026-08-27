using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactConsumeRarityAndCrafting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "crafting_count",
                table: "artifact_consume_observations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gold_price_paid",
                table: "artifact_consume_observations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rarity_achieved",
                table: "artifact_consume_observations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "crafting_count",
                table: "artifact_consume_observations");

            migrationBuilder.DropColumn(
                name: "gold_price_paid",
                table: "artifact_consume_observations");

            migrationBuilder.DropColumn(
                name: "rarity_achieved",
                table: "artifact_consume_observations");
        }
    }
}
