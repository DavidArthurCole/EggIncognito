using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations {
    public partial class AddEnvDesignVersionCascade : Migration {
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.Sql(
                "DELETE FROM env_design_versions AS v WHERE NOT EXISTS " +
                "(SELECT 1 FROM env_designs AS d WHERE d.id = v.design_id)");

            migrationBuilder.AddForeignKey(
                name: "FK_env_design_versions_env_designs_design_id",
                table: "env_design_versions",
                column: "design_id",
                principalTable: "env_designs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropForeignKey(
                name: "FK_env_design_versions_env_designs_design_id",
                table: "env_design_versions");
        }
    }
}
