using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ManualOutboundSliceA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "outbound_hours_end",
                table: "campaigns",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "outbound_hours_start",
                table: "campaigns",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "outbound_dial_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dialed_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    caller_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    callee_time_zone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    time_zone_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_dial_attempts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_dial_attempts_agent_created",
                table: "outbound_dial_attempts",
                columns: new[] { "agent_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_dial_attempts_created",
                table: "outbound_dial_attempts",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_dial_attempts");

            migrationBuilder.DropColumn(
                name: "outbound_hours_end",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "outbound_hours_start",
                table: "campaigns");
        }
    }
}
