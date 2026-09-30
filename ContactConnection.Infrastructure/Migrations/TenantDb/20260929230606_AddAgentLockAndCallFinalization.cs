using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddAgentLockAndCallFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "finalize_reason",
                table: "call_records",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finalized_at",
                table: "call_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "finalized_by_id",
                table: "call_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "finalized_by_name",
                table: "call_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "sign_in_locked",
                table: "agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "status_lock_reason",
                table: "agents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_locked_at",
                table: "agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status_locked_by_name",
                table: "agents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "finalize_reason",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "finalized_at",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "finalized_by_id",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "finalized_by_name",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "sign_in_locked",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "status_lock_reason",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "status_locked_at",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "status_locked_by_name",
                table: "agents");
        }
    }
}
