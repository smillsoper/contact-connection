using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class RecordingRetentionByDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recording_action",
                table: "dispositions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recording_retention_days",
                table: "dispositions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recording_action",
                table: "disposition_categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recording_retention_days",
                table: "disposition_categories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "unmapped_recording_retention_days",
                table: "campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recording_trimmed_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recording_trimmed_seconds",
                table: "call_records",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recording_action",
                table: "dispositions");

            migrationBuilder.DropColumn(
                name: "recording_retention_days",
                table: "dispositions");

            migrationBuilder.DropColumn(
                name: "recording_action",
                table: "disposition_categories");

            migrationBuilder.DropColumn(
                name: "recording_retention_days",
                table: "disposition_categories");

            migrationBuilder.DropColumn(
                name: "unmapped_recording_retention_days",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "recording_trimmed_at",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_trimmed_seconds",
                table: "call_records");
        }
    }
}
