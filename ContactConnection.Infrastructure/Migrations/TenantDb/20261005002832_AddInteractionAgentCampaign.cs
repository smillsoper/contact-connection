using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddInteractionAgentCampaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "agent_id",
                table: "call_interactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                table: "call_interactions",
                type: "uuid",
                nullable: true);

            // Backfill (S178): until now every interaction belonged to its record's agent and campaign.
            migrationBuilder.Sql(@"
                UPDATE call_interactions i
                SET agent_id = r.agent_id,
                    campaign_id = NULLIF(r.campaign_id, '00000000-0000-0000-0000-000000000000')
                FROM call_records r
                WHERE r.id = i.call_record_id AND i.agent_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "call_interactions");
        }
    }
}
