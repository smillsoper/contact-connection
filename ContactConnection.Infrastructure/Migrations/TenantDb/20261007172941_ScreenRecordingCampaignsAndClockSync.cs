using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ScreenRecordingCampaignsAndClockSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "clock_sync_rtt_ms",
                table: "screen_recordings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_height",
                table: "screen_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_width",
                table: "screen_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "screen_recording_enabled",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clock_sync_rtt_ms",
                table: "screen_recordings");

            migrationBuilder.DropColumn(
                name: "video_height",
                table: "screen_recordings");

            migrationBuilder.DropColumn(
                name: "video_width",
                table: "screen_recordings");

            migrationBuilder.DropColumn(
                name: "screen_recording_enabled",
                table: "campaigns");
        }
    }
}
