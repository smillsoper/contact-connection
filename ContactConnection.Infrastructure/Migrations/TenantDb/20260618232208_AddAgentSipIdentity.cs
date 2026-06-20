using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddAgentSipIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sip_a1hash",
                table: "agents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sip_extension",
                table: "agents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_agents_sip_extension",
                table: "agents",
                column: "sip_extension",
                unique: true,
                filter: "sip_extension IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_agents_sip_extension",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "sip_a1hash",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "sip_extension",
                table: "agents");
        }
    }
}
