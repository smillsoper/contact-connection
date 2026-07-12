using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCallTraceStateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "state_snapshot",
                table: "call_trace_events",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "state_snapshot",
                table: "call_trace_events");
        }
    }
}
