using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCallSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "call_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ai_summary = table.Column<string>(type: "text", nullable: false),
                    ai_reason_for_call = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ai_outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ai_disposition = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ai_disposition_valid = table.Column<bool>(type: "boolean", nullable: false),
                    ai_confidence = table.Column<double>(type: "double precision", nullable: false),
                    ai_follow_up = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ai_is_test_call = table.Column<bool>(type: "boolean", nullable: false),
                    possible_test_call = table.Column<bool>(type: "boolean", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    elapsed_ms = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    reason_for_call = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    disposition = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    follow_up = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    edited = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_call_summaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_call_summaries_call_records_call_record_id",
                        column: x => x.call_record_id,
                        principalTable: "call_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_call_summaries_call",
                table: "call_summaries",
                columns: new[] { "call_record_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_call_summaries_status",
                table: "call_summaries",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_summaries");
        }
    }
}
