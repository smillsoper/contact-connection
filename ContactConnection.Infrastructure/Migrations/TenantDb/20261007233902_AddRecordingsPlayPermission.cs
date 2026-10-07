using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddRecordingsPlayPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S183: recordings get their own permission. Calls.view alone (every agent) no longer plays them; the
            // built-in Administrator and Supervisor roles, and any role that can edit Call Records, keep that ability.
            migrationBuilder.Sql("""
                UPDATE roles SET permissions = permissions || '["recordings.play"]'::jsonb, updated_at = now()
                WHERE NOT permissions ? 'recordings.play'
                  AND ((is_built_in AND name IN ('Administrator', 'Supervisor')) OR permissions ? 'calls.manage');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE roles SET permissions = permissions - 'recordings.play';");
        }
    }
}
