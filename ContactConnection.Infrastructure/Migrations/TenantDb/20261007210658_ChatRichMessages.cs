using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ChatRichMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "body",
                table: "chat_messages",
                type: "character varying(40000)",
                maxLength: 40000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AddColumn<string>(
                name: "body_text",
                table: "chat_messages",
                type: "character varying(4020)",
                maxLength: 4020,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "chat_messages",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "text");

            // Existing (plain-text) messages: their words are their body.
            migrationBuilder.Sql("UPDATE chat_messages SET body_text = left(body, 4020) WHERE body_text = '';");

            migrationBuilder.CreateTable(
                name: "chat_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_files", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_files");

            migrationBuilder.DropColumn(
                name: "body_text",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "format",
                table: "chat_messages");

            migrationBuilder.AlterColumn<string>(
                name: "body",
                table: "chat_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40000)",
                oldMaxLength: 40000);
        }
    }
}
