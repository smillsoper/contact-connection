using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class InteractionScopedCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "interaction_id",
                table: "payment_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "interaction_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cart",
                table: "call_interactions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_number",
                table: "call_interactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "order_submitted_at",
                table: "call_interactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "call_interactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "routed_group_id",
                table: "call_interactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "routed_tier",
                table: "call_interactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "routed_tier_label",
                table: "call_interactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "call_interactions",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_amount",
                table: "call_interactions",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_interaction_id",
                table: "payment_transactions",
                column: "interaction_id");

            // S178 — interaction-scoped commerce. Until now the cart / order / payment / routing tier lived on the call
            // record and belonged to its first (usually only) piece of work: copy them onto the record's earliest
            // interaction, and link its payment transactions and orders to that interaction. Record columns stay for
            // now (read-only after phase 3) and are dropped in phase 5.
            migrationBuilder.Sql(@"
                WITH firsts AS (
                    SELECT DISTINCT ON (call_record_id) id, call_record_id FROM call_interactions
                    ORDER BY call_record_id, started_at NULLS LAST, interaction_number)
                UPDATE call_interactions i
                SET cart = r.cart, order_number = r.order_number, order_submitted_at = r.order_submitted_at,
                    total_amount = r.total_amount, tax_amount = r.tax_amount, payment_status = r.payment_status,
                    routed_group_id = r.routed_group_id, routed_tier = r.routed_tier, routed_tier_label = r.routed_tier_label
                FROM firsts f JOIN call_records r ON r.id = f.call_record_id
                WHERE i.id = f.id;");

            migrationBuilder.Sql(@"
                WITH firsts AS (
                    SELECT DISTINCT ON (call_record_id) id, call_record_id FROM call_interactions
                    ORDER BY call_record_id, started_at NULLS LAST, interaction_number)
                UPDATE payment_transactions t SET interaction_id = f.id
                FROM firsts f WHERE f.call_record_id = t.call_record_id AND t.interaction_id IS NULL;");

            migrationBuilder.Sql(@"
                WITH firsts AS (
                    SELECT DISTINCT ON (call_record_id) id, call_record_id FROM call_interactions
                    ORDER BY call_record_id, started_at NULLS LAST, interaction_number)
                UPDATE orders o SET interaction_id = f.id
                FROM firsts f WHERE f.call_record_id = o.call_record_id AND o.interaction_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_interaction_id",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "interaction_id",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "interaction_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cart",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "order_number",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "order_submitted_at",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "routed_group_id",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "routed_tier",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "routed_tier_label",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "call_interactions");

            migrationBuilder.DropColumn(
                name: "total_amount",
                table: "call_interactions");
        }
    }
}
