using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSipGateways : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sip_gateways",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    proxy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    from_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    register = table.Column<bool>(type: "boolean", nullable: false),
                    transport = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    codec_prefs = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sip_gateways", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sip_gateways_name",
                schema: "public",
                table: "sip_gateways",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sip_gateways_tenant_id",
                schema: "public",
                table: "sip_gateways",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sip_gateways",
                schema: "public");
        }
    }
}
