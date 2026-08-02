using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "upload_batch_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_id = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: true),
                    proto_sha = table.Column<string>(type: "text", nullable: true),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    build = table.Column<string>(type: "text", nullable: true),
                    client_version = table.Column<string>(type: "text", nullable: true),
                    diagnostics = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_batch_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_batches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    submitted_by = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_items = table.Column<int>(type: "integer", nullable: false),
                    processed_items = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_batches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_upload_batch_items_batch_id_status",
                table: "upload_batch_items",
                columns: new[] { "batch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_upload_batches_status",
                table: "upload_batches",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upload_batch_items");

            migrationBuilder.DropTable(
                name: "upload_batches");
        }
    }
}
