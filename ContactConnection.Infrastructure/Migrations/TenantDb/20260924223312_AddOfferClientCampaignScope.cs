using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddOfferClientCampaignScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                table: "offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_offers_scope",
                table: "offers",
                columns: new[] { "tenant_id", "product_id", "client_id", "campaign_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offers_scope",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "offers");
        }
    }
}
