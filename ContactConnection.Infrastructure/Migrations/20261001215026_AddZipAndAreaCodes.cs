using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddZipAndAreaCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "area_codes",
                schema: "public",
                columns: table => new
                {
                    area_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    zip_count = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_area_codes", x => x.area_code);
                });

            migrationBuilder.CreateTable(
                name: "zip_codes",
                schema: "public",
                columns: table => new
                {
                    zip = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    county = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    area_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zip_codes", x => x.zip);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "area_codes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "zip_codes",
                schema: "public");
        }
    }
}
