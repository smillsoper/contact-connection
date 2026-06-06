using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorApiTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "api_type",
                schema: "public",
                table: "portal_api_definitions",
                newName: "api_category");

            migrationBuilder.RenameIndex(
                name: "ix_portal_api_definitions_api_type_active",
                schema: "public",
                table: "portal_api_definitions",
                newName: "ix_portal_api_definitions_api_category_active");

            migrationBuilder.RenameIndex(
                name: "ix_portal_api_definitions_api_type",
                schema: "public",
                table: "portal_api_definitions",
                newName: "ix_portal_api_definitions_api_category");

            migrationBuilder.AddColumn<string>(
                name: "api_sub_type",
                schema: "public",
                table: "portal_api_endpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_preferred",
                schema: "public",
                table: "portal_api_endpoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_portal_api_endpoints_api_sub_type",
                schema: "public",
                table: "portal_api_endpoints",
                column: "api_sub_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_portal_api_endpoints_api_sub_type",
                schema: "public",
                table: "portal_api_endpoints");

            migrationBuilder.DropColumn(
                name: "api_sub_type",
                schema: "public",
                table: "portal_api_endpoints");

            migrationBuilder.DropColumn(
                name: "is_preferred",
                schema: "public",
                table: "portal_api_endpoints");

            migrationBuilder.RenameColumn(
                name: "api_category",
                schema: "public",
                table: "portal_api_definitions",
                newName: "api_type");

            migrationBuilder.RenameIndex(
                name: "ix_portal_api_definitions_api_category_active",
                schema: "public",
                table: "portal_api_definitions",
                newName: "ix_portal_api_definitions_api_type_active");

            migrationBuilder.RenameIndex(
                name: "ix_portal_api_definitions_api_category",
                schema: "public",
                table: "portal_api_definitions",
                newName: "ix_portal_api_definitions_api_type");
        }
    }
}
