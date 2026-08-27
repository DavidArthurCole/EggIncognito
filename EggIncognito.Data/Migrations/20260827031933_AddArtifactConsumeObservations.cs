using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactConsumeObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artifact_consume_observations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    action = table.Column<string>(type: "text", nullable: false),
                    spec_name = table.Column<string>(type: "text", nullable: false),
                    spec_level = table.Column<string>(type: "text", nullable: false),
                    spec_rarity = table.Column<string>(type: "text", nullable: false),
                    count_requested = table.Column<int>(type: "integer", nullable: false),
                    byproducts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    other_rewards = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    golden_eggs = table.Column<double>(type: "double precision", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    client_version = table.Column<string>(type: "text", nullable: true),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifact_consume_observations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_artifact_consume_observations_spec_name_spec_level_spec_rar~",
                table: "artifact_consume_observations",
                columns: new[] { "spec_name", "spec_level", "spec_rarity", "action" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifact_consume_observations");
        }
    }
}
