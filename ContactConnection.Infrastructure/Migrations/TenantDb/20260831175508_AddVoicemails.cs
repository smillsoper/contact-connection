using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddVoicemails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voicemails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transcription = table.Column<string>(type: "text", nullable: true),
                    email_delivery_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email_delivered_to = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email_delivery_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    email_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    heard_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heard_by = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voicemails", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_voicemails_call_record",
                table: "voicemails",
                column: "call_record_id");

            migrationBuilder.CreateIndex(
                name: "idx_voicemails_campaign_status",
                table: "voicemails",
                columns: new[] { "campaign_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voicemails");
        }
    }
}
