using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ApiSandboxEnvironment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sandbox_path",
                table: "tenant_api_endpoints",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_base_url",
                table: "tenant_api_definitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_token_url",
                table: "tenant_api_definitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "training_uses_sandbox",
                table: "tenant_api_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sandbox_path",
                table: "tenant_api_endpoints");

            migrationBuilder.DropColumn(
                name: "sandbox_base_url",
                table: "tenant_api_definitions");

            migrationBuilder.DropColumn(
                name: "sandbox_token_url",
                table: "tenant_api_definitions");

            migrationBuilder.DropColumn(
                name: "training_uses_sandbox",
                table: "tenant_api_definitions");
        }
    }
}
