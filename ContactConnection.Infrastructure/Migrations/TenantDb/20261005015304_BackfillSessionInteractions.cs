using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class BackfillSessionInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S178: every script session runs inside a saved interaction (cart, order, payments, disposition, AI summary
            // belong to it). Manual launches sent no interaction id (all-zero), and only queue delivery ever saved
            // interaction rows; give each such past session its own interaction, numbered after the call's existing ones.
            migrationBuilder.Sql(@"
                UPDATE flow_sessions SET interaction_id = gen_random_uuid()
                WHERE interaction_id = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.Sql(@"
                INSERT INTO call_interactions (id, call_record_id, interaction_number, type, flow_id, flow_version,
                    commitment_events, flow_execution_state, started_at, completed_at, status, agent_id, campaign_id)
                SELECT x.interaction_id, x.call_record_id,
                       COALESCE((SELECT MAX(i.interaction_number) FROM call_interactions i WHERE i.call_record_id = x.call_record_id), 0)
                         + ROW_NUMBER() OVER (PARTITION BY x.call_record_id ORDER BY x.started_at),
                       'customer_service', x.flow_id, x.flow_version, '[]'::jsonb, '{}'::jsonb,
                       x.started_at, x.completed_at,
                       CASE WHEN x.status = 'complete' THEN 'complete' ELSE 'incomplete' END,
                       x.agent_id, NULLIF(r.campaign_id, '00000000-0000-0000-0000-000000000000')
                FROM (SELECT DISTINCT ON (s.interaction_id) s.* FROM flow_sessions s
                      WHERE NOT EXISTS (SELECT 1 FROM call_interactions i WHERE i.id = s.interaction_id)
                      ORDER BY s.interaction_id, s.started_at) x
                JOIN call_records r ON r.id = x.call_record_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
