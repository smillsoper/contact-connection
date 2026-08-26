using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <summary>
    /// Also carries call_state_history.met_service_level — EF diffs the whole model against
    /// whatever's already changed in code at `migrations add` time, and both entity changes
    /// (Campaign.RingStrategy/RingTopN, CallStateHistoryEntry.MetServiceLevel) landed together
    /// here since a planned follow-up migration for the latter came up empty.
    /// </summary>
    public partial class AddCampaignRingStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ring_strategy",
                table: "campaigns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ring_all");

            migrationBuilder.AddColumn<int>(
                name: "ring_top_n",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "met_service_level",
                table: "call_state_history",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ring_strategy",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "ring_top_n",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "met_service_level",
                table: "call_state_history");
        }
    }
}
