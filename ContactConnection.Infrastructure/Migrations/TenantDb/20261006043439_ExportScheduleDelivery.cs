using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ExportScheduleDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "deliver",
                table: "export_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "file_deleted_at",
                table: "export_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_for",
                table: "export_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_targets",
                table: "export_definitions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_scheduled_for",
                table: "export_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "schedule",
                table: "export_definitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "export_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_test = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    sent_as = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_deliveries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_export_runs_scheduled",
                table: "export_runs",
                columns: new[] { "definition_id", "scheduled_for" },
                unique: true,
                filter: "scheduled_for IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_export_audit_definition",
                table: "export_audit_entries",
                columns: new[] { "definition_id", "at" });

            migrationBuilder.CreateIndex(
                name: "idx_export_deliveries_queue",
                table: "export_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "idx_export_deliveries_run",
                table: "export_deliveries",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_audit_entries");

            migrationBuilder.DropTable(
                name: "export_deliveries");

            migrationBuilder.DropIndex(
                name: "ux_export_runs_scheduled",
                table: "export_runs");

            migrationBuilder.DropColumn(
                name: "deliver",
                table: "export_runs");

            migrationBuilder.DropColumn(
                name: "file_deleted_at",
                table: "export_runs");

            migrationBuilder.DropColumn(
                name: "scheduled_for",
                table: "export_runs");

            migrationBuilder.DropColumn(
                name: "delivery_targets",
                table: "export_definitions");

            migrationBuilder.DropColumn(
                name: "last_scheduled_for",
                table: "export_definitions");

            migrationBuilder.DropColumn(
                name: "schedule",
                table: "export_definitions");
        }
    }
}
