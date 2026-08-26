using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_api_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signature_header_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    signature_algorithm = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    include_timestamp = table.Column<bool>(type: "boolean", nullable: false),
                    timestamp_tolerance_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    raw_body = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processing_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    outcome_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_api_endpoint_id",
                table: "webhook_endpoints",
                column: "tenant_api_endpoint_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_token",
                table: "webhook_endpoints",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_endpoint_body_hash",
                table: "webhook_events",
                columns: new[] { "webhook_endpoint_id", "body_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_webhook_endpoint_id",
                table: "webhook_events",
                column: "webhook_endpoint_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_endpoints");

            migrationBuilder.DropTable(
                name: "webhook_events");
        }
    }
}
