using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCallRecordingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_mask_on_hold",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "consent_model",
                table: "campaigns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "one_party");

            migrationBuilder.AddColumn<bool>(
                name: "record_stereo",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "recording_beep_enabled",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "recording_mode",
                table: "campaigns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "disabled");

            migrationBuilder.AddColumn<bool>(
                name: "recording_required",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "recording_retention_days",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<string>(
                name: "recording_delete_reason",
                table: "call_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recording_deleted_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recording_events",
                table: "call_records",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "recording_masked_seconds",
                table: "call_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "recording_retained",
                table: "call_records",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recording_started_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recording_stopped_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_mask_on_hold",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "consent_model",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "record_stereo",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "recording_beep_enabled",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "recording_mode",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "recording_required",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "recording_retention_days",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "recording_delete_reason",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_deleted_at",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_events",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_masked_seconds",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_retained",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_started_at",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "recording_stopped_at",
                table: "call_records");
        }
    }
}
