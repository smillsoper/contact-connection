using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class RenameCallbacksToScheduledCallbacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "callbacks");

            migrationBuilder.CreateTable(
                name: "scheduled_callbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    callback_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dnis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    caller_id_override = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_flow_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outbound_call_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_callbacks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_scheduled_callbacks_call_record",
                table: "scheduled_callbacks",
                column: "call_record_id");

            migrationBuilder.CreateIndex(
                name: "idx_scheduled_callbacks_campaign_status",
                table: "scheduled_callbacks",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_scheduled_callbacks_status_scheduled",
                table: "scheduled_callbacks",
                columns: new[] { "status", "scheduled_for" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_callbacks");

            migrationBuilder.CreateTable(
                name: "callbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    callback_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    caller_id_override = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    dnis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    outbound_call_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_callbacks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_callbacks_call_record",
                table: "callbacks",
                column: "call_record_id");

            migrationBuilder.CreateIndex(
                name: "idx_callbacks_campaign_status",
                table: "callbacks",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_callbacks_status_scheduled",
                table: "callbacks",
                columns: new[] { "status", "scheduled_for" });
        }
    }
}
