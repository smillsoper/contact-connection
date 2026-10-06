using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreditDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credit_disposition",
                schema: "public",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "applied_credit_note_id",
                schema: "public",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_applied_credit_note",
                schema: "public",
                table: "invoice_lines",
                column: "applied_credit_note_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_applied_credit_note",
                schema: "public",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "credit_disposition",
                schema: "public",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "applied_credit_note_id",
                schema: "public",
                table: "invoice_lines");
        }
    }
}
