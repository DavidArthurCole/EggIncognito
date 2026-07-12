using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class RepointCaptureTablesToUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM capture_proxy_addrs WHERE user_id = '00000000-0000-0000-0000-000000000000' OR user_id IS NULL) THEN
                        RAISE EXCEPTION 'capture_proxy_addrs has unbackfilled user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM capture_user_cas WHERE user_id = '00000000-0000-0000-0000-000000000000' OR user_id IS NULL) THEN
                        RAISE EXCEPTION 'capture_user_cas has unbackfilled user_id rows - run the backfill tool before this migration';
                    END IF;
                END $$;
            ");

            migrationBuilder.DropPrimaryKey(
                name: "PK_capture_user_cas",
                table: "capture_user_cas");

            migrationBuilder.DropPrimaryKey(
                name: "PK_capture_proxy_addrs",
                table: "capture_proxy_addrs");

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "capture_user_cas",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "capture_proxy_addrs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_capture_user_cas",
                table: "capture_user_cas",
                column: "user_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_capture_proxy_addrs",
                table: "capture_proxy_addrs",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_capture_user_cas",
                table: "capture_user_cas");

            migrationBuilder.DropPrimaryKey(
                name: "PK_capture_proxy_addrs",
                table: "capture_proxy_addrs");

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "capture_user_cas",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "capture_proxy_addrs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_capture_user_cas",
                table: "capture_user_cas",
                column: "discord_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_capture_proxy_addrs",
                table: "capture_proxy_addrs",
                column: "discord_id");
        }
    }
}
