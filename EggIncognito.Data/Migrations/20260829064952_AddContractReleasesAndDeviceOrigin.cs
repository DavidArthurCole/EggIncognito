using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractReleasesAndDeviceOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "devices",
                type: "text",
                nullable: false,
                defaultValue: "runtime");

            migrationBuilder.CreateTable(
                name: "contract_releases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contract_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    egg = table.Column<int>(type: "integer", nullable: false),
                    custom_egg_id = table.Column<string>(type: "text", nullable: true),
                    season_id = table.Column<string>(type: "text", nullable: true),
                    start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    length_seconds = table.Column<double>(type: "double precision", nullable: false),
                    leggacy = table.Column<bool>(type: "boolean", nullable: false),
                    ultra_only = table.Column<bool>(type: "boolean", nullable: false),
                    prophecy_eggs = table.Column<int>(type: "integer", nullable: false),
                    coop_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    max_coop_size = table.Column<int>(type: "integer", nullable: false),
                    minutes_per_token = table.Column<double>(type: "double precision", nullable: false),
                    proto = table.Column<byte[]>(type: "bytea", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_releases", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contract_releases_contract_id",
                table: "contract_releases",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "IX_contract_releases_contract_id_start_time",
                table: "contract_releases",
                columns: new[] { "contract_id", "start_time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contract_releases_start_time",
                table: "contract_releases",
                column: "start_time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_releases");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "devices");
        }
    }
}
