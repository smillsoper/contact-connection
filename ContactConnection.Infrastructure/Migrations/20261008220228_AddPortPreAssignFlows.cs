using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortPreAssignFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pre_assign_flow_id",
                schema: "public",
                table: "port_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pre_assign_telephony_flow_id",
                schema: "public",
                table: "port_orders",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pre_assign_flow_id",
                schema: "public",
                table: "port_orders");

            migrationBuilder.DropColumn(
                name: "pre_assign_telephony_flow_id",
                schema: "public",
                table: "port_orders");
        }
    }
}
