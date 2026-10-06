using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class CommissionByDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "disposition_category_id",
                table: "commission_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "disposition_id",
                table: "commission_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disposition_label",
                table: "commission_rules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disposition_category_id",
                table: "commission_rules");

            migrationBuilder.DropColumn(
                name: "disposition_id",
                table: "commission_rules");

            migrationBuilder.DropColumn(
                name: "disposition_label",
                table: "commission_rules");
        }
    }
}
