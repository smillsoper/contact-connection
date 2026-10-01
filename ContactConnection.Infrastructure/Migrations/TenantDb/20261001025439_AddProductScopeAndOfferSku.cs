using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddProductScopeAndOfferSku : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sku",
                table: "offers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_scope",
                table: "products",
                columns: new[] { "client_id", "campaign_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_scope",
                table: "products");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "sku",
                table: "offers");
        }
    }
}
