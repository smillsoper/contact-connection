using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddWebhookEventsCascadeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_webhook_events_webhook_endpoints_webhook_endpoint_id",
                table: "webhook_events",
                column: "webhook_endpoint_id",
                principalTable: "webhook_endpoints",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_webhook_events_webhook_endpoints_webhook_endpoint_id",
                table: "webhook_events");
        }
    }
}
