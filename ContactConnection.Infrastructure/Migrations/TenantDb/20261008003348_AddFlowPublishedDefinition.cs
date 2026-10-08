using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddFlowPublishedDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                table: "flows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_definition",
                table: "flows",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "published_version",
                table: "flows",
                type: "integer",
                nullable: true);
            // Every flow that's live today keeps running exactly what it runs now: its current script becomes the
            // published copy. Unpublished flows stay drafts.
            migrationBuilder.Sql("""
                UPDATE flows SET published_definition = definition, published_version = version, published_at = updated_at
                WHERE is_active;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "published_at",
                table: "flows");

            migrationBuilder.DropColumn(
                name: "published_definition",
                table: "flows");

            migrationBuilder.DropColumn(
                name: "published_version",
                table: "flows");
        }
    }
}
