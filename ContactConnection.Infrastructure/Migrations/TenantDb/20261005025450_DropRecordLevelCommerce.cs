using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class DropRecordLevelCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cart",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "order_number",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "order_submitted_at",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "routed_group_id",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "routed_tier",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "routed_tier_label",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "total_amount",
                table: "call_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cart",
                table: "call_records",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_number",
                table: "call_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "order_submitted_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "call_records",
                type: "character varying(30)",
                maxLength: 30,
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

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "call_records",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_amount",
                table: "call_records",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }
    }
}
