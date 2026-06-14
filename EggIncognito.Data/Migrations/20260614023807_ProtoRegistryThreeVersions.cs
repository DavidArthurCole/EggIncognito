using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtoRegistryThreeVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "version",
                table: "proto_versions",
                newName: "build");

            migrationBuilder.RenameIndex(
                name: "IX_proto_versions_platform_version",
                table: "proto_versions",
                newName: "IX_proto_versions_platform_build");

            migrationBuilder.AddColumn<string>(
                name: "app_version",
                table: "proto_versions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "client_version",
                table: "proto_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "proto_versions",
                type: "text",
                nullable: false,
                defaultValue: "farm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "app_version",
                table: "proto_versions");

            migrationBuilder.DropColumn(
                name: "client_version",
                table: "proto_versions");

            migrationBuilder.DropColumn(
                name: "source",
                table: "proto_versions");

            migrationBuilder.RenameColumn(
                name: "build",
                table: "proto_versions",
                newName: "version");

            migrationBuilder.RenameIndex(
                name: "IX_proto_versions_platform_build",
                table: "proto_versions",
                newName: "IX_proto_versions_platform_version");
        }
    }
}
