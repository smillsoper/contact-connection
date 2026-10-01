using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddMediaAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "media_attribution",
                table: "call_records",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_agencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    fields = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_agencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    media_agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    market = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    media_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ad_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_default_local = table.Column<bool>(type: "boolean", nullable: false),
                    field_values = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_assignments_media_agencies_media_agency_id",
                        column: x => x.media_agency_id,
                        principalTable: "media_agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_assignments_phone_numbers_phone_number_id",
                        column: x => x.phone_number_id,
                        principalTable: "phone_numbers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_agencies_name",
                table: "media_agencies",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assignments_media_agency_id",
                table: "media_assignments",
                column: "media_agency_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_assignments_number_start",
                table: "media_assignments",
                columns: new[] { "phone_number_id", "start_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assignments");

            migrationBuilder.DropTable(
                name: "media_agencies");

            migrationBuilder.DropColumn(
                name: "media_attribution",
                table: "call_records");
        }
    }
}
