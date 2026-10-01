using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddMediaAssignmentStationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "station_facility_id",
                table: "media_assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "station_latitude",
                table: "media_assignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "station_longitude",
                table: "media_assignments",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "station_facility_id",
                table: "media_assignments");

            migrationBuilder.DropColumn(
                name: "station_latitude",
                table: "media_assignments");

            migrationBuilder.DropColumn(
                name: "station_longitude",
                table: "media_assignments");
        }
    }
}
