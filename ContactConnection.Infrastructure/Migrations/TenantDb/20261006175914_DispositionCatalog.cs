using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class DispositionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "disposition",
                table: "call_interactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "disposition_id",
                table: "call_interactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "disposition_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sales_opportunity = table.Column<bool>(type: "boolean", nullable: false),
                    excluded_from_kpis = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disposition_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispositions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aliases = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispositions", x => x.id);
                    table.ForeignKey(
                        name: "FK_dispositions_disposition_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "disposition_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_call_interactions_disposition",
                table: "call_interactions",
                column: "disposition_id");

            migrationBuilder.CreateIndex(
                name: "ux_disposition_categories_key",
                table: "disposition_categories",
                column: "key",
                unique: true,
                filter: "key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_dispositions_scope",
                table: "dispositions",
                columns: new[] { "campaign_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_dispositions_category_id",
                table: "dispositions",
                column: "category_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispositions");

            migrationBuilder.DropTable(
                name: "disposition_categories");

            migrationBuilder.DropIndex(
                name: "idx_call_interactions_disposition",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "disposition_id",
                table: "call_interactions");

            migrationBuilder.AlterColumn<string>(
                name: "disposition",
                table: "call_interactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
