using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAgentInvitesToTenantAdminInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "agent_invites",
                schema: "public",
                newName: "tenant_admin_invites",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_agent_invites_token",
                schema: "public",
                table: "tenant_admin_invites",
                newName: "IX_tenant_admin_invites_token");

            migrationBuilder.RenameIndex(
                name: "IX_agent_invites_tenant_id",
                schema: "public",
                table: "tenant_admin_invites",
                newName: "IX_tenant_admin_invites_tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "tenant_admin_invites",
                schema: "public",
                newName: "agent_invites",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_tenant_admin_invites_token",
                schema: "public",
                table: "agent_invites",
                newName: "IX_agent_invites_token");

            migrationBuilder.RenameIndex(
                name: "IX_tenant_admin_invites_tenant_id",
                schema: "public",
                table: "agent_invites",
                newName: "IX_agent_invites_tenant_id");
        }
    }
}
