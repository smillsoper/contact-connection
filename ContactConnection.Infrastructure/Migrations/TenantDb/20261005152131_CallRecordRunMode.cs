using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class CallRecordRunMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credential_set",
                table: "call_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "production");

            migrationBuilder.AddColumn<string>(
                name: "run_mode",
                table: "call_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "production");

            // S179 launch modes: manual stubs were always practice runs (designer "Select flow → Start"); before this they
            // used production credentials, so record exactly that.
            migrationBuilder.Sql(@"UPDATE call_records SET run_mode = 'sandbox', credential_set = 'production' WHERE source = 'manual';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credential_set",
                table: "call_records");

            migrationBuilder.DropColumn(
                name: "run_mode",
                table: "call_records");
        }
    }
}
