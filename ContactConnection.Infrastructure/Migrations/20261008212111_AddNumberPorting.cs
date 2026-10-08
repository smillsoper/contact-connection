using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNumberPorting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signalwire_project_id",
                schema: "public",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "port_orders",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1001', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    numbers = table.Column<List<string>>(type: "text[]", nullable: false),
                    services = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    end_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    current_provider_hint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pre_assign_campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_by_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    signer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    signer = table.Column<string>(type: "jsonb", nullable: true),
                    pin_protected = table.Column<string>(type: "text", nullable: true),
                    pin_not_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    bill_file_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    bill_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bill_content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    signature_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    signer_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    signer_user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    loa_file_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    loa_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    certificate_file_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correction_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    signalwire_order_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    foc_date = table.Column<DateOnly>(type: "date", nullable: true),
                    numbers_loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sensitive_purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    events = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_port_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_port_orders_status",
                schema: "public",
                table: "port_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_port_orders_tenant_created",
                schema: "public",
                table: "port_orders",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_port_orders_token_hash",
                schema: "public",
                table: "port_orders",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ux_port_orders_number",
                schema: "public",
                table: "port_orders",
                column: "number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "port_orders",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "signalwire_project_id",
                schema: "public",
                table: "tenants");
        }
    }
}
