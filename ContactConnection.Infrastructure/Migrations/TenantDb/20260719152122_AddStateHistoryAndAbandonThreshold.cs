using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddStateHistoryAndAbandonThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "short_abandon_threshold_seconds",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "agent_state_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    custom_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_state_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "call_state_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    abandon_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    abandon_length = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_call_state_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_state_history_agent_id_entered_at",
                table: "agent_state_history",
                columns: new[] { "agent_id", "entered_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_state_history_tenant_id_entered_at",
                table: "agent_state_history",
                columns: new[] { "tenant_id", "entered_at" });

            migrationBuilder.CreateIndex(
                name: "IX_call_state_history_call_record_id_sequence",
                table: "call_state_history",
                columns: new[] { "call_record_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_call_state_history_tenant_id_campaign_id_entered_at",
                table: "call_state_history",
                columns: new[] { "tenant_id", "campaign_id", "entered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_state_history");

            migrationBuilder.DropTable(
                name: "call_state_history");

            migrationBuilder.DropColumn(
                name: "short_abandon_threshold_seconds",
                table: "campaigns");
        }
    }
}
