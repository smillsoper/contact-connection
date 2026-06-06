using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class RefactorApiTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "api_type",
                table: "tenant_api_definitions",
                newName: "api_category");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_api_definitions_api_type_active",
                table: "tenant_api_definitions",
                newName: "ix_tenant_api_definitions_api_category_active");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_api_definitions_api_type",
                table: "tenant_api_definitions",
                newName: "ix_tenant_api_definitions_api_category");

            migrationBuilder.AddColumn<string>(
                name: "api_sub_type",
                table: "tenant_api_endpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_preferred",
                table: "tenant_api_endpoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tenant_api_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_sub_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    portal_api_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_api_preferences", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_endpoints_api_sub_type",
                table: "tenant_api_endpoints",
                column: "api_sub_type");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_preferences_api_sub_type",
                table: "tenant_api_preferences",
                column: "api_sub_type",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_api_preferences");

            migrationBuilder.DropIndex(
                name: "ix_tenant_api_endpoints_api_sub_type",
                table: "tenant_api_endpoints");

            migrationBuilder.DropColumn(
                name: "api_sub_type",
                table: "tenant_api_endpoints");

            migrationBuilder.DropColumn(
                name: "is_preferred",
                table: "tenant_api_endpoints");

            migrationBuilder.RenameColumn(
                name: "api_category",
                table: "tenant_api_definitions",
                newName: "api_type");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_api_definitions_api_category_active",
                table: "tenant_api_definitions",
                newName: "ix_tenant_api_definitions_api_type_active");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_api_definitions_api_category",
                table: "tenant_api_definitions",
                newName: "ix_tenant_api_definitions_api_type");
        }
    }
}
