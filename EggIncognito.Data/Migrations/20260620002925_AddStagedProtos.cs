using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStagedProtos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staged_protos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    build = table.Column<string>(type: "text", nullable: true),
                    client_version = table.Column<string>(type: "text", nullable: true),
                    package = table.Column<string>(type: "text", nullable: true),
                    proto_sha = table.Column<string>(type: "text", nullable: false),
                    proto_text = table.Column<string>(type: "text", nullable: false),
                    message_index = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    submitted_by = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    origin_repo = table.Column<string>(type: "text", nullable: true),
                    origin_commit = table.Column<string>(type: "text", nullable: true),
                    origin_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staged_protos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staged_protos_proto_sha",
                table: "staged_protos",
                column: "proto_sha");

            migrationBuilder.CreateIndex(
                name: "IX_staged_protos_status",
                table: "staged_protos",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staged_protos");
        }
    }
}
