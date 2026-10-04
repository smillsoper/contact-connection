using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBillingRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "billing_monthly_minimum",
                schema: "public",
                table: "tenants",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "billing_rate_per_minute",
                schema: "public",
                table: "tenants",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "billing_toll_free_surcharge",
                schema: "public",
                table: "tenants",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_monthly_minimum",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "billing_rate_per_minute",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "billing_toll_free_surcharge",
                schema: "public",
                table: "tenants");
        }
    }
}
