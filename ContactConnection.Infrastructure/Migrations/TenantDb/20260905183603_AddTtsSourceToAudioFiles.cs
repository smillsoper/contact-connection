using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContactConnection.Infrastructure.Migrations.TenantDb
{
    /// <inheritdoc />
    public partial class AddTtsSourceToAudioFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tts_provider_key",
                table: "audio_files",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tts_source_text",
                table: "audio_files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tts_voice_id",
                table: "audio_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tts_provider_key",
                table: "audio_files");

            migrationBuilder.DropColumn(
                name: "tts_source_text",
                table: "audio_files");

            migrationBuilder.DropColumn(
                name: "tts_voice_id",
                table: "audio_files");
        }
    }
}
