using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class RenameTenantApiPreferenceToSourceEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "portal_api_endpoint_id",
                table: "tenant_api_preferences",
                newName: "endpoint_id");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "tenant_api_preferences",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source",
                table: "tenant_api_preferences");

            migrationBuilder.RenameColumn(
                name: "endpoint_id",
                table: "tenant_api_preferences",
                newName: "portal_api_endpoint_id");
        }
    }
}
