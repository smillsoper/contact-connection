using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddTenantApiDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_api_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: false),
                    query_params = table.Column<string>(type: "jsonb", nullable: false),
                    request_body_template = table.Column<string>(type: "text", nullable: true),
                    response_mapping = table.Column<string>(type: "jsonb", nullable: false),
                    auth_config = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_api_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_definitions_api_type",
                table: "tenant_api_definitions",
                column: "api_type");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_definitions_api_type_active",
                table: "tenant_api_definitions",
                columns: new[] { "api_type", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_api_definitions");
        }
    }
}
