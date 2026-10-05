using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class CallSummaryInteraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "interaction_id",
                table: "call_summaries",
                type: "uuid",
                nullable: true);

            // S178: summaries become per-interaction; existing ones summarized the call's first (only) piece of work.
            migrationBuilder.Sql(@"
                WITH firsts AS (
                    SELECT DISTINCT ON (call_record_id) id, call_record_id FROM call_interactions
                    ORDER BY call_record_id, started_at NULLS LAST, interaction_number)
                UPDATE call_summaries s SET interaction_id = f.id
                FROM firsts f WHERE f.call_record_id = s.call_record_id AND s.interaction_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "interaction_id",
                table: "call_summaries");
        }
    }
}
