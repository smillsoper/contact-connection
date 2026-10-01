using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBroadcastStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broadcast_stations",
                schema: "public",
                columns: table => new
                {
                    facility_id = table.Column<int>(type: "integer", nullable: false),
                    call_sign = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    service_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    community_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    community_state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    network_affiliation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broadcast_stations", x => x.facility_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_stations_call_sign",
                schema: "public",
                table: "broadcast_stations",
                column: "call_sign");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcast_stations",
                schema: "public");
        }
    }
}
