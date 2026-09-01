using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddRecordingMergeJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recording_merge_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    output_blob_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    output_format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    output_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    had_video = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    screen_recording_id = table.Column<Guid>(type: "uuid", nullable: true),
                    screen_recording_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ffmpeg_command = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_merge_jobs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_recording_merge_jobs_call_record",
                table: "recording_merge_jobs",
                column: "call_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_recording_merge_jobs_status_next",
                table: "recording_merge_jobs",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recording_merge_jobs");
        }
    }
}
