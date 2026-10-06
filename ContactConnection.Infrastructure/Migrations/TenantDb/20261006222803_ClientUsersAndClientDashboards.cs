using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class ClientUsersAndClientDashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_client_dashboard",
                table: "dashboards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "scope_campaign_ids",
                table: "dashboards",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "scope_client_id",
                table: "dashboards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "client_user_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    by_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_user_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    can_play_recordings = table.Column<bool>(type: "boolean", nullable: false),
                    invite_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    invite_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    default_dashboard_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_agent_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_user_dashboards",
                columns: table => new
                {
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_user_dashboards", x => new { x.client_user_id, x.dashboard_id });
                    table.ForeignKey(
                        name: "FK_client_user_dashboards_client_users_client_user_id",
                        column: x => x.client_user_id,
                        principalTable: "client_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_client_user_dashboards_dashboards_dashboard_id",
                        column: x => x.dashboard_id,
                        principalTable: "dashboards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_user_audit_at",
                table: "client_user_audit",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "IX_client_user_audit_client_user_id_at",
                table: "client_user_audit",
                columns: new[] { "client_user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_client_user_dashboards_dashboard_id",
                table: "client_user_dashboards",
                column: "dashboard_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_users_email",
                table: "client_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_users_invite_token_hash",
                table: "client_users",
                column: "invite_token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_user_audit");

            migrationBuilder.DropTable(
                name: "client_user_dashboards");

            migrationBuilder.DropTable(
                name: "client_users");

            migrationBuilder.DropColumn(
                name: "is_client_dashboard",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "scope_campaign_ids",
                table: "dashboards");

            migrationBuilder.DropColumn(
                name: "scope_client_id",
                table: "dashboards");
        }
    }
}
