using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropUserIdentitySessionModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identities");

            migrationBuilder.DropTable(
                name: "revoked_sessions");

            migrationBuilder.DropTable(
                name: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identities",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identities", x => new { x.provider, x.subject });
                });

            migrationBuilder.CreateTable(
                name: "revoked_sessions",
                columns: table => new
                {
                    sid = table.Column<string>(type: "text", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revoked_sessions", x => x.sid);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    avatar = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    discord_id = table.Column<string>(type: "text", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false, defaultValue: "viewer"),
                    username = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identities_user_id",
                table: "identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_discord_id",
                table: "users",
                column: "discord_id",
                unique: true,
                filter: "discord_id IS NOT NULL");
        }
    }
}
