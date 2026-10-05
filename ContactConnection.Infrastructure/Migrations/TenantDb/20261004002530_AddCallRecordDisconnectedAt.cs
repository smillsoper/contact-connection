using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCallRecordDisconnectedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disconnected_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill (S175). The caller's hang-up is the terminal call state, unless the startup orphan sweep wrote it:
            // there was no hang-up then, so leave the time unknown. Same for manual cleanup rows.
            migrationBuilder.Sql(@"
                UPDATE call_records r SET disconnected_at = h.ended
                FROM (SELECT call_record_id, MAX(entered_at) AS ended FROM call_state_history
                      WHERE state IN ('completed', 'abandoned')
                        AND COALESCE(detail, '') NOT LIKE 'Reconciled on API startup%'
                        -- Hand-written cleanup rows (dev debugging, S118+) aren't hang-ups either (S179: they billed days).
                        AND COALESCE(detail, '') NOT ILIKE '%cleanup%'
                      GROUP BY call_record_id) h
                WHERE h.call_record_id = r.id AND r.disconnected_at IS NULL;");

            // Softphone-dialed outbound records have no state timeline; the softphone closed them at hang-up.
            migrationBuilder.Sql(@"
                UPDATE call_records SET disconnected_at = call_end_at
                WHERE disconnected_at IS NULL AND source = 'outbound' AND call_end_at IS NOT NULL
                  -- Before S169 a softphone-dialed record stayed open until the next startup sweep closed it: hours or days
                  -- later is not a hang-up time (S179). Leave those unknown (held for review, not billed).
                  AND call_end_at - call_start_at < interval '1 hour';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disconnected_at",
                table: "call_records");
        }
    }
}
