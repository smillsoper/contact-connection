using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class InteractionAgentGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_group_ids",
                table: "call_interactions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            // Backfill: interactions from before the snapshot get the group they were routed through plus their agent's
            // groups as of this migration — the best record there is for past calls.
            migrationBuilder.Sql("""
                UPDATE call_interactions i
                SET agent_group_ids = coalesce((
                    SELECT jsonb_agg(DISTINCT g)
                    FROM (
                        SELECT m.group_id AS g FROM agent_group_members m WHERE m.agent_id = i.agent_id
                        UNION
                        SELECT i.routed_group_id WHERE i.routed_group_id IS NOT NULL
                    ) x
                ), '[]'::jsonb)
                WHERE i.agent_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_group_ids",
                table: "call_interactions");
        }
    }
}
