using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameEndpointUrlToBaseUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "endpoint_url",
                schema: "public",
                table: "portal_api_definitions",
                newName: "base_url");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "base_url",
                schema: "public",
                table: "portal_api_definitions",
                newName: "endpoint_url");
        }
    }
}
