using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "filters",
                table: "feed_subscriptions",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "feed_deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE feed_subscriptions SET filters = " +
                "'{require_client_version,require_proto,sane_build,known_delta}' " +
                "WHERE event_kind = 'proto_build';");

            migrationBuilder.CreateTable(
                name: "feed_suppressions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    event_kind = table.Column<string>(type: "text", nullable: false, defaultValue: "proto_build"),
                    dedup_key = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feed_suppressions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_feed_suppressions_subscription_id_created_at",
                table: "feed_suppressions",
                columns: new[] { "subscription_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed_suppressions");

            migrationBuilder.DropColumn(
                name: "filters",
                table: "feed_subscriptions");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "feed_deliveries");
        }
    }
}
