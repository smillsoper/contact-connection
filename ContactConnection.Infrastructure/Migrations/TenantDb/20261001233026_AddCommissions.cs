using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddCommissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "commissions_reversed_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commissions_reversed_reason",
                table: "call_records",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "order_submitted_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "commission_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    basis = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_commission_entries_call_records_call_record_id",
                        column: x => x.call_record_id,
                        principalTable: "call_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "commission_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    field_value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tier_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commission_entries_agent_time",
                table: "commission_entries",
                columns: new[] { "agent_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_entries_call",
                table: "commission_entries",
                column: "call_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_entries_time",
                table: "commission_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_campaign",
                table: "commission_rules",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_client",
                table: "commission_rules",
                column: "client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_entries");

            migrationBuilder.DropTable(
                name: "commission_rules");

            migrationBuilder.DropColumn(
                name: "commissions_reversed_at",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "commissions_reversed_reason",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "order_submitted_at",
                table: "call_records");
        }
    }
}
