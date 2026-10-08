using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "health_checks",
                schema: "public",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    warn_override = table.Column<double>(type: "double precision", nullable: true),
                    crit_override = table.Column<double>(type: "double precision", nullable: true),
                    muted_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_alerted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_alerted_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_checks", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "health_incidents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "health_samples",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_samples", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "health_settings",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    extra_recipients = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                schema: "public",
                columns: table => new
                {
                    entra_oid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_users", x => x.entra_oid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_health_incidents_started",
                schema: "public",
                table: "health_incidents",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_health_samples_at",
                schema: "public",
                table: "health_samples",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_health_samples_key_at",
                schema: "public",
                table: "health_samples",
                columns: new[] { "key", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "health_checks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "health_incidents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "health_samples",
                schema: "public");

            migrationBuilder.DropTable(
                name: "health_settings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "platform_users",
                schema: "public");
        }
    }
}
