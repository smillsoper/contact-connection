using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCampaignTelephonySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "after_call_work_seconds",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "caller_id_number",
                table: "campaigns",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "campaigns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "queue_acceleration_enabled",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "queue_acceleration_interval_seconds",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "queue_acceleration_priority_boost",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "after_call_work_seconds",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "caller_id_number",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "queue_acceleration_enabled",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "queue_acceleration_interval_seconds",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "queue_acceleration_priority_boost",
                table: "campaigns");
        }
    }
}
