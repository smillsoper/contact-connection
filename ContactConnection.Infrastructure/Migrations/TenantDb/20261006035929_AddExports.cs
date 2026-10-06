using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "export_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    spec = table.Column<string>(type: "jsonb", nullable: false),
                    spec_revision = table.Column<int>(type: "integer", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_vendor_contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approval_recorded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approval_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    approved_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_spec_revision = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    spec_revision = table.Column<int>(type: "integer", nullable: false),
                    spec = table.Column<string>(type: "jsonb", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_test = table.Column<bool>(type: "boolean", nullable: false),
                    data_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    row_count = table.Column<int>(type: "integer", nullable: true),
                    call_count = table.Column<int>(type: "integer", nullable: true),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    blob_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_export_definitions_name",
                table: "export_definitions",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "idx_export_runs_definition",
                table: "export_runs",
                columns: new[] { "definition_id", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "idx_export_runs_queue",
                table: "export_runs",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_definitions");

            migrationBuilder.DropTable(
                name: "export_runs");
        }
    }
}
