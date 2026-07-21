using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationEventKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "event_kind",
                table: "feed_subscriptions",
                type: "text",
                nullable: false,
                defaultValue: "proto_build");

            migrationBuilder.AddColumn<string>(
                name: "dedup_key",
                table: "feed_deliveries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "event_kind",
                table: "feed_deliveries",
                type: "text",
                nullable: false,
                defaultValue: "proto_build");

            migrationBuilder.Sql(
                "UPDATE feed_deliveries SET dedup_key = proto_version_id::text WHERE dedup_key = '';");

            migrationBuilder.DropIndex(
                name: "IX_feed_deliveries_subscription_id_proto_version_id",
                table: "feed_deliveries");

            migrationBuilder.DropColumn(
                name: "proto_version_id",
                table: "feed_deliveries");

            migrationBuilder.CreateIndex(
                name: "IX_feed_deliveries_subscription_id_event_kind_dedup_key",
                table: "feed_deliveries",
                columns: new[] { "subscription_id", "event_kind", "dedup_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_feed_deliveries_subscription_id_event_kind_dedup_key",
                table: "feed_deliveries");

            migrationBuilder.DropColumn(
                name: "event_kind",
                table: "feed_subscriptions");

            migrationBuilder.DropColumn(
                name: "dedup_key",
                table: "feed_deliveries");

            migrationBuilder.DropColumn(
                name: "event_kind",
                table: "feed_deliveries");

            migrationBuilder.AddColumn<int>(
                name: "proto_version_id",
                table: "feed_deliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_feed_deliveries_subscription_id_proto_version_id",
                table: "feed_deliveries",
                columns: new[] { "subscription_id", "proto_version_id" },
                unique: true);
        }
    }
}
