using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdAndIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // Add user_id to users, backfill one UUID per row, before touching the PK.
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            // New identities table.
            migrationBuilder.CreateTable(
                name: "identities",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identities", x => new { x.provider, x.subject });
                });

            migrationBuilder.CreateIndex(
                name: "IX_identities_user_id",
                table: "identities",
                column: "user_id");

            // Backfill identities from existing discord_id rows.
            migrationBuilder.Sql(
                "INSERT INTO identities (user_id, provider, subject) " +
                "SELECT user_id, 'discord', discord_id FROM users WHERE discord_id IS NOT NULL " +
                "ON CONFLICT (provider, subject) DO NOTHING;");

            // Add user_id to the two discord_id-keyed tables, backfill by matching discord_id.
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "capture_proxy_addrs",
                type: "uuid",
                nullable: true);
            migrationBuilder.Sql(
                "UPDATE capture_proxy_addrs SET user_id = u.user_id FROM users u " +
                "WHERE capture_proxy_addrs.discord_id = u.discord_id AND capture_proxy_addrs.user_id IS NULL;");
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "capture_proxy_addrs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "capture_user_cas",
                type: "uuid",
                nullable: true);
            migrationBuilder.Sql(
                "UPDATE capture_user_cas SET user_id = u.user_id FROM users u " +
                "WHERE capture_user_cas.discord_id = u.discord_id AND capture_user_cas.user_id IS NULL;");
            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "capture_user_cas",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // Repoint users' PK to user_id, make discord_id nullable with a partial unique index.
            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "user_id");

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_users_discord_id",
                table: "users",
                column: "discord_id",
                unique: true,
                filter: "discord_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_discord_id",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            // Per-row placeholder, not a constant '': two or more Authentik-only users (no
            // Discord identity) rolling back together would otherwise collide on the same blank
            // string once discord_id becomes the sole PK again.
            migrationBuilder.Sql("UPDATE users SET discord_id = 'authentik-only:' || user_id WHERE discord_id IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "discord_id");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "capture_user_cas");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "capture_proxy_addrs");

            migrationBuilder.DropTable(
                name: "identities");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "users");
        }
    }
}
