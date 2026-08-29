using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContributedCaptures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contributed_captures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contributor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'"),
                    dedupe_hash = table.Column<string>(type: "text", nullable: false),
                    client_version = table.Column<string>(type: "text", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contributed_captures", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contributed_captures_contributor_user_id_dedupe_hash",
                table: "contributed_captures",
                columns: new[] { "contributor_user_id", "dedupe_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contributed_captures_contributor_user_id_status",
                table: "contributed_captures",
                columns: new[] { "contributor_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_contributed_captures_kind",
                table: "contributed_captures",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "IX_contributed_captures_status_recorded_at",
                table: "contributed_captures",
                columns: new[] { "status", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contributed_captures");
        }
    }
}
