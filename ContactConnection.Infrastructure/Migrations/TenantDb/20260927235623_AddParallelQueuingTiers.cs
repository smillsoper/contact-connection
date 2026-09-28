using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddParallelQueuingTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "exclusive_window_seconds",
                table: "group_campaign_assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "routing_tier",
                table: "group_campaign_assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "tier_label",
                table: "group_campaign_assignments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "routed_group_id",
                table: "call_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "routed_tier",
                table: "call_records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "routed_tier_label",
                table: "call_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_group_member_campaign_exclusions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_group_member_campaign_exclusions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_group_member_campaign_exclusions_campaign_id_group_id",
                table: "agent_group_member_campaign_exclusions",
                columns: new[] { "campaign_id", "group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_group_member_campaign_exclusions_group_id_agent_id_ca~",
                table: "agent_group_member_campaign_exclusions",
                columns: new[] { "group_id", "agent_id", "campaign_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_group_member_campaign_exclusions");

            migrationBuilder.DropColumn(
                name: "exclusive_window_seconds",
                table: "group_campaign_assignments");

            migrationBuilder.DropColumn(
                name: "routing_tier",
                table: "group_campaign_assignments");

            migrationBuilder.DropColumn(
                name: "tier_label",
                table: "group_campaign_assignments");

            migrationBuilder.DropColumn(
                name: "routed_group_id",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "routed_tier",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "routed_tier_label",
                table: "call_records");
        }
    }
}
