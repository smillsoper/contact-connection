using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointLiquidAndSuccessCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "body_template_type",
                schema: "public",
                table: "portal_api_endpoints",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "simple");

            migrationBuilder.AddColumn<string>(
                name: "success_criteria",
                schema: "public",
                table: "portal_api_endpoints",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "body_template_type",
                schema: "public",
                table: "portal_api_endpoints");

            migrationBuilder.DropColumn(
                name: "success_criteria",
                schema: "public",
                table: "portal_api_endpoints");
        }
    }
}
