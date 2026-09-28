using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddExternalRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_routing_accept_mode",
                table: "campaigns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "queue_count");

            migrationBuilder.AddColumn<int>(
                name: "external_routing_limit",
                table: "campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_routing_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_session_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dnis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ani = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    accepted = table.Column<bool>(type: "boolean", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    targets = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reported_call_date = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_routing_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_routing_requests_campaign_id_created_at",
                table: "external_routing_requests",
                columns: new[] { "campaign_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_external_routing_requests_number_provider_id_kind_created_at",
                table: "external_routing_requests",
                columns: new[] { "number_provider_id", "kind", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_routing_requests");

            migrationBuilder.DropColumn(
                name: "external_routing_accept_mode",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "external_routing_limit",
                table: "campaigns");
        }
    }
}
