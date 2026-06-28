using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleToAdminInvite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                schema: "public",
                table: "tenant_admin_invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role_name",
                schema: "public",
                table: "tenant_admin_invites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role_id",
                schema: "public",
                table: "tenant_admin_invites");

            migrationBuilder.DropColumn(
                name: "role_name",
                schema: "public",
                table: "tenant_admin_invites");
        }
    }
}
