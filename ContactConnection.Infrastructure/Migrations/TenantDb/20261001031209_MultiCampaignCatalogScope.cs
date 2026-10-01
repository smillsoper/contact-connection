using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class MultiCampaignCatalogScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Single campaign scope → a list of campaigns (S169). Add the arrays first and copy each
            // existing campaign_id into them, so scoped offers/products keep their scope; then drop.
            migrationBuilder.DropIndex(name: "ix_products_scope", table: "products");
            migrationBuilder.DropIndex(name: "ix_offers_scope", table: "offers");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "campaign_ids", table: "products", type: "uuid[]", nullable: false, defaultValueSql: "'{}'");
            migrationBuilder.AddColumn<List<Guid>>(
                name: "campaign_ids", table: "offers", type: "uuid[]", nullable: false, defaultValueSql: "'{}'");

            migrationBuilder.Sql("UPDATE products SET campaign_ids = ARRAY[campaign_id] WHERE campaign_id IS NOT NULL;");
            migrationBuilder.Sql("UPDATE offers SET campaign_ids = ARRAY[campaign_id] WHERE campaign_id IS NOT NULL;");

            migrationBuilder.DropColumn(name: "campaign_id", table: "products");
            migrationBuilder.DropColumn(name: "campaign_id", table: "offers");

            migrationBuilder.CreateIndex(name: "ix_products_scope", table: "products", column: "client_id");
            migrationBuilder.CreateIndex(
                name: "ix_offers_scope", table: "offers", columns: new[] { "tenant_id", "product_id", "client_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to a single campaign: keeps the first listed campaign (a multi-campaign scope narrows).
            migrationBuilder.DropIndex(name: "ix_products_scope", table: "products");
            migrationBuilder.DropIndex(name: "ix_offers_scope", table: "offers");

            migrationBuilder.AddColumn<Guid>(name: "campaign_id", table: "products", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "campaign_id", table: "offers", type: "uuid", nullable: true);

            migrationBuilder.Sql("UPDATE products SET campaign_id = campaign_ids[1] WHERE cardinality(campaign_ids) > 0;");
            migrationBuilder.Sql("UPDATE offers SET campaign_id = campaign_ids[1] WHERE cardinality(campaign_ids) > 0;");

            migrationBuilder.DropColumn(name: "campaign_ids", table: "products");
            migrationBuilder.DropColumn(name: "campaign_ids", table: "offers");

            migrationBuilder.CreateIndex(
                name: "ix_products_scope", table: "products", columns: new[] { "client_id", "campaign_id" });
            migrationBuilder.CreateIndex(
                name: "ix_offers_scope", table: "offers", columns: new[] { "tenant_id", "product_id", "client_id", "campaign_id" });
        }
    }
}
