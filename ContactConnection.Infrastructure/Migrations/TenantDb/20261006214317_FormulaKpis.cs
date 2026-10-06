using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class FormulaKpis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "custom_kpis",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "percent");

            migrationBuilder.AddColumn<string>(
                name: "formula",
                table: "custom_kpis",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "custom_kpis",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ratio");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "format",
                table: "custom_kpis");

            migrationBuilder.DropColumn(
                name: "formula",
                table: "custom_kpis");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "custom_kpis");
        }
    }
}
