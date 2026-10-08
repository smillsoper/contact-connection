using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddNumbersPortPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S184: number porting — the built-in Administrator role can request port-ins.
            migrationBuilder.Sql("""
                UPDATE roles SET permissions = permissions || '["numbers.port"]'::jsonb, updated_at = now()
                WHERE NOT permissions ? 'numbers.port' AND is_built_in AND name = 'Administrator';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE roles SET permissions = permissions - 'numbers.port';");
        }
    }
}
