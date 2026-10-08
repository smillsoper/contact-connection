using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddHelpdesks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "helpdesk_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    helpdesk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_image = table.Column<bool>(type: "boolean", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_helpdesk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "helpdesk_topics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    helpdesk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    html = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    attachment_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_helpdesk_topics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "helpdesks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    campaign_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_helpdesks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_helpdesk_files_helpdesk",
                table: "helpdesk_files",
                column: "helpdesk_id");

            migrationBuilder.CreateIndex(
                name: "ix_helpdesk_topics_helpdesk_order",
                table: "helpdesk_topics",
                columns: new[] { "helpdesk_id", "sort_order" });

            // S184: the built-in Administrator role manages help desks.
            migrationBuilder.Sql("""
                UPDATE roles SET permissions = permissions || '["helpdesk.manage"]'::jsonb, updated_at = now()
                WHERE NOT permissions ? 'helpdesk.manage' AND is_built_in AND name = 'Administrator';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE roles SET permissions = permissions - 'helpdesk.manage';");

            migrationBuilder.DropTable(
                name: "helpdesk_files");

            migrationBuilder.DropTable(
                name: "helpdesk_topics");

            migrationBuilder.DropTable(
                name: "helpdesks");
        }
    }
}
