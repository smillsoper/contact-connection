using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ReserveNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "campaign_id",
                table: "phone_numbers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reserved_at",
                table: "phone_numbers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_phone_numbers_campaign_id_reserved_at",
                table: "phone_numbers",
                columns: new[] { "campaign_id", "reserved_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_phone_numbers_campaign_id_reserved_at",
                table: "phone_numbers");

            migrationBuilder.DropColumn(
                name: "reserved_at",
                table: "phone_numbers");

            migrationBuilder.AlterColumn<Guid>(
                name: "campaign_id",
                table: "phone_numbers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
