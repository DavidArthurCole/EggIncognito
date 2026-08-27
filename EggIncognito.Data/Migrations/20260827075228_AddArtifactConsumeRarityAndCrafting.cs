using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations;

public partial class AddArtifactConsumeRarityAndCrafting : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<string>(
            name: "rarity_achieved",
            table: "artifact_consume_observations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "gold_price_paid",
            table: "artifact_consume_observations",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "crafting_count",
            table: "artifact_consume_observations",
            type: "integer",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
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
