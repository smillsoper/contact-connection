using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddNumberProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_number",
                table: "phone_numbers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_id",
                table: "phone_numbers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "phone_numbers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "hosted");

            migrationBuilder.AddColumn<string>(
                name: "client_number",
                table: "call_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "number_provider_id",
                table: "call_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "number_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sip_gateway_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_ips = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    api_key_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    api_key_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_number_providers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_phone_numbers_provider_id_client_number",
                table: "phone_numbers",
                columns: new[] { "provider_id", "client_number" });

            migrationBuilder.CreateIndex(
                name: "IX_number_providers_api_key_hash",
                table: "number_providers",
                column: "api_key_hash",
                unique: true,
                filter: "api_key_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_number_providers_name",
                table: "number_providers",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_providers");

            migrationBuilder.DropIndex(
                name: "IX_phone_numbers_provider_id_client_number",
                table: "phone_numbers");

            migrationBuilder.DropColumn(
                name: "client_number",
                table: "phone_numbers");

            migrationBuilder.DropColumn(
                name: "provider_id",
                table: "phone_numbers");

            migrationBuilder.DropColumn(
                name: "role",
                table: "phone_numbers");

            migrationBuilder.DropColumn(
                name: "client_number",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "number_provider_id",
                table: "call_records");
        }
    }
}
