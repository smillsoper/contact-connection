using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ChatAttachmentsAndChannelMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "attachment_ids",
                table: "chat_messages",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<string>(
                name: "attachments",
                table: "chat_messages",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "mentions_channel",
                table: "chat_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "content_type",
                table: "chat_files",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "chat_files",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "chat_files",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "image");

            migrationBuilder.CreateIndex(
                name: "ix_chat_files_created",
                table: "chat_files",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_files_created",
                table: "chat_files");

            migrationBuilder.DropColumn(
                name: "attachment_ids",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "attachments",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "mentions_channel",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "file_name",
                table: "chat_files");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "chat_files");

            migrationBuilder.AlterColumn<string>(
                name: "content_type",
                table: "chat_files",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);
        }
    }
}
