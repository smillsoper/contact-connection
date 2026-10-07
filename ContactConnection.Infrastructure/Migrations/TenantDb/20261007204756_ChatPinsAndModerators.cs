using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ChatPinsAndModerators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pinned_at",
                table: "chat_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pinned_by_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "moderator_ids",
                table: "chat_channels",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "moderator_role_ids",
                table: "chat_channels",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "pinner_ids",
                table: "chat_channels",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "pinner_role_ids",
                table: "chat_channels",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.CreateTable(
                name: "chat_personal_pins",
                columns: table => new
                {
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_personal_pins", x => new { x.agent_id, x.message_id });
                    table.ForeignKey(
                        name: "FK_chat_personal_pins_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_personal_pins_agent_channel",
                table: "chat_personal_pins",
                columns: new[] { "agent_id", "channel_id" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_personal_pins_message_id",
                table: "chat_personal_pins",
                column: "message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_personal_pins");

            migrationBuilder.DropColumn(
                name: "pinned_at",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "pinned_by_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "moderator_ids",
                table: "chat_channels");

            migrationBuilder.DropColumn(
                name: "moderator_role_ids",
                table: "chat_channels");

            migrationBuilder.DropColumn(
                name: "pinner_ids",
                table: "chat_channels");

            migrationBuilder.DropColumn(
                name: "pinner_role_ids",
                table: "chat_channels");
        }
    }
}
