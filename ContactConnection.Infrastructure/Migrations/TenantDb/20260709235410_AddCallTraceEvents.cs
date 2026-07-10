using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCallTraceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dnis",
                table: "call_records",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "call_trace_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    engine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    node_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    node_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    transition_taken = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    exit_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    next_node_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flow_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dnis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ani = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_call_trace_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_call_trace_events_call_record_id_sequence",
                table: "call_trace_events",
                columns: new[] { "call_record_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_call_trace_events_tenant_id_campaign_id",
                table: "call_trace_events",
                columns: new[] { "tenant_id", "campaign_id" });

            migrationBuilder.CreateIndex(
                name: "IX_call_trace_events_tenant_id_dnis",
                table: "call_trace_events",
                columns: new[] { "tenant_id", "dnis" });

            migrationBuilder.CreateIndex(
                name: "IX_call_trace_events_tenant_id_flow_id",
                table: "call_trace_events",
                columns: new[] { "tenant_id", "flow_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_trace_events");

            migrationBuilder.DropColumn(
                name: "dnis",
                table: "call_records");
        }
    }
}
