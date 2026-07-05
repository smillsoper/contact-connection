using System.Diagnostics;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ContactConnection.Api.Endpoints;

public static class AudioFilesEndpoints
{
    private static readonly HashSet<string> AllowedExtensions =
        [".wav", ".mp3", ".ogg", ".webm", ".mp4", ".m4a", ".flac", ".aac"];

    public static void MapAudioFilesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audio-files").RequireAuthorization();

        // POST /api/v1/audio-files — multipart upload; transcodes to OGG Vorbis 8 kHz mono via ffmpeg
        group.MapPost("/", async (
            HttpContext http,
            IWebHostEnvironment env,
            IConfiguration config,
            ITenantDbContextFactory dbFactory,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            if (!http.Request.HasFormContentType)
                return Results.BadRequest(new { error = "Multipart form upload required." });

            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file provided." });

            var uploadExt = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(uploadExt))
                return Results.BadRequest(new { error = "Unsupported audio format. Accepted: WAV, MP3, OGG, WebM, MP4, M4A, FLAC, AAC." });

            var name = form["name"].FirstOrDefault()?.Trim()
                ?? Path.GetFileNameWithoutExtension(file.FileName);

            var schemaName = tenantContext.Current.SchemaName;
            var soundsHostPath = ResolveSoundsHostPath(config, env);
            var tenantDir = Path.Combine(soundsHostPath, schemaName);
            Directory.CreateDirectory(tenantDir);

            var audioId   = Guid.NewGuid();
            var tempPath  = Path.Combine(tenantDir, $"{audioId}_tmp{uploadExt}");
            var oggName   = $"{audioId}.ogg";
            var oggPath   = Path.Combine(tenantDir, oggName);

            // Save the raw upload to a temp file
            await using (var stream = File.Create(tempPath))
                await file.CopyToAsync(stream, ct);

            // Transcode to OGG Vorbis 8 kHz mono — the format FreeSWITCH mod_sndfile handles natively
            long oggSize;
            try
            {
                var ffmpegExe = config["FreeSWITCH:FfmpegPath"] ?? "ffmpeg";
                var psi = new ProcessStartInfo(ffmpegExe)
                {
                    Arguments        = $"-y -i \"{tempPath}\" -vn -ar 8000 -ac 1 -c:a libvorbis -q:a 3 \"{oggPath}\"",
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("ffmpeg could not be started. Ensure it is installed and in PATH (or set FreeSWITCH:FfmpegPath in config).");

                // Read stderr concurrently — avoids pipe-buffer deadlock when stderr > 4 KB
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                var stderr = await stderrTask;

                if (proc.ExitCode != 0 || !File.Exists(oggPath))
                    throw new InvalidOperationException($"ffmpeg exited with code {proc.ExitCode}. stderr: {stderr.TrimEnd()}");

                oggSize = new FileInfo(oggPath).Length;
            }
            catch (Exception ex)
            {
                if (File.Exists(oggPath)) File.Delete(oggPath);
                return Results.Problem($"Audio transcoding failed: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            await using var db = dbFactory.Create(tenantContext.Current!.SchemaName);
            var audioFile = AudioFile.Create(
                tenantContext.Current.Id,
                name,
                file.FileName,
                oggName,
                "audio/ogg",
                oggSize);

            db.AudioFiles.Add(audioFile);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/audio-files/{audioFile.Id}", ToResponse(audioFile));
        }).DisableAntiforgery();

        // GET /api/v1/audio-files — list
        group.MapGet("/", async (
            ITenantDbContextFactory dbFactory,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            await using var db = dbFactory.Create(tenantContext.Current!.SchemaName);
            var files = await db.AudioFiles
                .OrderBy(f => f.Name)
                .ToListAsync(ct);

            return Results.Ok(files.Select(ToResponse));
        });

        // DELETE /api/v1/audio-files/{id}
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IWebHostEnvironment env,
            IConfiguration config,
            ITenantDbContextFactory dbFactory,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            await using var db = dbFactory.Create(tenantContext.Current!.SchemaName);
            var audioFile = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (audioFile is null) return Results.NotFound();

            var schemaName = tenantContext.Current.SchemaName;
            var soundsHostPath = ResolveSoundsHostPath(config, env);
            var diskPath = Path.Combine(soundsHostPath, schemaName, audioFile.StoredFileName);
            if (File.Exists(diskPath))
                File.Delete(diskPath);

            db.AudioFiles.Remove(audioFile);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // GET /api/v1/audio-files/{id}/stream — serve file for browser preview
        group.MapGet("/{id:guid}/stream", async (
            Guid id,
            IWebHostEnvironment env,
            IConfiguration config,
            ITenantDbContextFactory dbFactory,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            await using var db = dbFactory.Create(tenantContext.Current!.SchemaName);
            var audioFile = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (audioFile is null) return Results.NotFound();

            var schemaName = tenantContext.Current.SchemaName;
            var soundsHostPath = ResolveSoundsHostPath(config, env);
            var diskPath = Path.Combine(soundsHostPath, schemaName, audioFile.StoredFileName);

            if (!File.Exists(diskPath))
                return Results.NotFound();

            var contentType = audioFile.ContentType;
            return Results.File(diskPath, contentType, audioFile.OriginalFileName);
        });
    }

    private static string ResolveSoundsHostPath(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["FreeSWITCH:SoundsHostPath"];
        if (!string.IsNullOrEmpty(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        }
        // Default: {ApiProjectRoot}/../freeswitch/sounds → repo root's freeswitch/sounds
        return Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "freeswitch", "sounds"));
    }

    private static object ToResponse(AudioFile f) => new
    {
        id             = f.Id,
        name           = f.Name,
        originalFileName = f.OriginalFileName,
        contentType    = f.ContentType,
        fileSizeBytes  = f.FileSizeBytes,
        createdAt      = f.CreatedAt,
    };
}
