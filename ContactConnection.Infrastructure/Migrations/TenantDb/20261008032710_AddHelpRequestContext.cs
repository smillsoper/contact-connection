using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddHelpRequestContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "context",
                table: "help_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_key",
                table: "help_requests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "context",
                table: "help_requests");

            migrationBuilder.DropColumn(
                name: "snapshot_key",
                table: "help_requests");
        }
    }
}
