using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class RebuildWebhooksAsCanonicalMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_webhook_endpoints_tenant_api_endpoint_id",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "tenant_api_endpoint_id",
                table: "webhook_endpoints");

            migrationBuilder.AddColumn<string>(
                name: "canonical_type",
                table: "webhook_endpoints",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "webhook_endpoints",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mapping_config",
                table: "webhook_endpoints",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "webhook_endpoints",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_canonical_type",
                table: "webhook_endpoints",
                column: "canonical_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_webhook_endpoints_canonical_type",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "canonical_type",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "description",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "mapping_config",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "name",
                table: "webhook_endpoints");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_api_endpoint_id",
                table: "webhook_endpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_api_endpoint_id",
                table: "webhook_endpoints",
                column: "tenant_api_endpoint_id",
                unique: true);
        }
    }
}
