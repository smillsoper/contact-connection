using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StripeBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "autopay_enabled",
                schema: "public",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_method_id",
                schema: "public",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method_label",
                schema: "public",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method_type",
                schema: "public",
                table: "tenants",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_customer_id",
                schema: "public",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_attempts",
                schema: "public",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "payment_error",
                schema: "public",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_state",
                schema: "public",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_payment_intent_id",
                schema: "public",
                table: "invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_stripe_payment_intent",
                schema: "public",
                table: "invoices",
                column: "stripe_payment_intent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_stripe_payment_intent",
                schema: "public",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "autopay_enabled",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "payment_method_id",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "payment_method_label",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "payment_method_type",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "stripe_customer_id",
                schema: "public",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "payment_attempts",
                schema: "public",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "payment_error",
                schema: "public",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "payment_state",
                schema: "public",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "stripe_payment_intent_id",
                schema: "public",
                table: "invoices");
        }
    }
}
