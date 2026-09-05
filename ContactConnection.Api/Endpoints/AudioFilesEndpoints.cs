using System.Diagnostics;
using ContactConnection.Application.Interfaces.Services;
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
            var tenantDir  = TenantAudioDir(config, env, schemaName);
            Directory.CreateDirectory(tenantDir);

            var audioId  = Guid.NewGuid();
            var tempPath = Path.Combine(tenantDir, $"{audioId}_tmp{uploadExt}");
            var oggName  = $"{audioId}.ogg";
            var oggPath  = Path.Combine(tenantDir, oggName);

            // Save the raw upload to a temp file
            await using (var stream = File.Create(tempPath))
                await file.CopyToAsync(stream, ct);

            long oggSize;
            try
            {
                oggSize = await TranscodeToOggAsync(config, tempPath, oggPath, ct);
            }
            catch (Exception ex)
            {
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

        // POST /api/v1/audio-files/tts — synthesize the tenant's configured TTS vendor voice once
        // and save it as a new, named, reusable audio file (a "saved TTS clip") — appears in every
        // existing AudioFilePicker exactly like an upload, no node-handler changes needed. Requires
        // the tenant to have an ApiSubType.TtsStreaming preference configured (see
        // GET /api/v1/telephony/tts-service-status); this is the vendor path only — Flite is
        // rendered inline by FreeSWITCH at playback time and has nothing to pre-synthesize.
        group.MapPost("/tts", async (
            SaveTtsClipRequest req,
            IWebHostEnvironment env,
            IConfiguration config,
            ITenantDbContextFactory dbFactory,
            ITtsStreamingService ttsStreaming,
            ITtsFileSynthesizer ttsSynth,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name is required." });
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Text is required." });
            if (string.IsNullOrWhiteSpace(req.VoiceId)) return Results.BadRequest(new { error = "VoiceId is required." });

            var tenant = tenantContext.Current;
            var provider = await ttsStreaming.ResolveProviderAsync(tenant.SchemaName, ct);
            if (provider is null)
                return Results.BadRequest(new { error = "No TTS vendor is configured for this tenant — configure one in Preferences first." });

            var synthesized = await ttsSynth.SynthesizeToBytesAsync(
                tenant.Subdomain, provider.ProviderKey, provider.SettingsJson, req.VoiceId, req.Text, ct);
            if (synthesized is null)
                // 400, not 502 — a failed synthesis (bad voice id, missing vendor credentials,
                // vendor rejected the request) is a client-actionable input problem, not a gateway/
                // infrastructure failure. Cloudflare (and most reverse proxies) intercepts a raw 502
                // from the origin and substitutes its own branded error page in place of the actual
                // body, silently hiding this exact message from the caller.
                return Results.BadRequest(new { error = "TTS synthesis failed — check the voice ID and the vendor's credentials/status." });

            var tenantDir = TenantAudioDir(config, env, tenant.SchemaName);
            Directory.CreateDirectory(tenantDir);

            var audioId  = Guid.NewGuid();
            var wavPath  = Path.Combine(tenantDir, $"{audioId}_tmp.wav");
            var oggName  = $"{audioId}.ogg";
            var oggPath  = Path.Combine(tenantDir, oggName);

            long oggSize;
            try
            {
                await File.WriteAllBytesAsync(wavPath, WriteWavBytes(synthesized.Value.Wav, synthesized.Value.SampleRateHz), ct);
                oggSize = await TranscodeToOggAsync(config, wavPath, oggPath, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Audio transcoding failed: {ex.Message}");
            }
            finally
            {
                if (File.Exists(wavPath)) File.Delete(wavPath);
            }

            await using var db = dbFactory.Create(tenant.SchemaName);
            var audioFile = AudioFile.CreateFromTts(
                tenant.Id, req.Name.Trim(), oggName, "audio/ogg", oggSize,
                req.Text, provider.ProviderKey, req.VoiceId);

            db.AudioFiles.Add(audioFile);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/audio-files/{audioFile.Id}", ToResponse(audioFile));
        });

        // PUT /api/v1/audio-files/{id}/tts — re-synthesize a saved TTS clip in place (same id, so
        // every node still referencing it by audioFileId keeps working); rejects a plain
        // uploaded/recorded file (use the regular upload endpoint + a new file for those).
        group.MapPut("/{id:guid}/tts", async (
            Guid id,
            SaveTtsClipRequest req,
            IWebHostEnvironment env,
            IConfiguration config,
            ITenantDbContextFactory dbFactory,
            ITtsStreamingService ttsStreaming,
            ITtsFileSynthesizer ttsSynth,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Text is required." });
            if (string.IsNullOrWhiteSpace(req.VoiceId)) return Results.BadRequest(new { error = "VoiceId is required." });

            var tenant = tenantContext.Current;
            await using var db = dbFactory.Create(tenant.SchemaName);
            var audioFile = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (audioFile is null) return Results.NotFound();
            if (!audioFile.IsTtsGenerated)
                return Results.BadRequest(new { error = "This audio file wasn't created from TTS and can't be regenerated." });

            var provider = await ttsStreaming.ResolveProviderAsync(tenant.SchemaName, ct);
            if (provider is null)
                return Results.BadRequest(new { error = "No TTS vendor is configured for this tenant — configure one in Preferences first." });

            var synthesized = await ttsSynth.SynthesizeToBytesAsync(
                tenant.Subdomain, provider.ProviderKey, provider.SettingsJson, req.VoiceId, req.Text, ct);
            if (synthesized is null)
                // 400, not 502 — a failed synthesis (bad voice id, missing vendor credentials,
                // vendor rejected the request) is a client-actionable input problem, not a gateway/
                // infrastructure failure. Cloudflare (and most reverse proxies) intercepts a raw 502
                // from the origin and substitutes its own branded error page in place of the actual
                // body, silently hiding this exact message from the caller.
                return Results.BadRequest(new { error = "TTS synthesis failed — check the voice ID and the vendor's credentials/status." });

            var tenantDir = TenantAudioDir(config, env, tenant.SchemaName);
            Directory.CreateDirectory(tenantDir);

            // Same StoredFileName as before — overwrite in place, no dangling old file, no id change.
            var wavPath = Path.Combine(tenantDir, $"{audioFile.Id}_tmp.wav");
            var oggPath = Path.Combine(tenantDir, audioFile.StoredFileName);

            long oggSize;
            try
            {
                await File.WriteAllBytesAsync(wavPath, WriteWavBytes(synthesized.Value.Wav, synthesized.Value.SampleRateHz), ct);
                oggSize = await TranscodeToOggAsync(config, wavPath, oggPath, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Audio transcoding failed: {ex.Message}");
            }
            finally
            {
                if (File.Exists(wavPath)) File.Delete(wavPath);
            }

            audioFile.RegenerateFromTts(
                "audio/ogg", oggSize, req.Text, provider.ProviderKey, req.VoiceId, req.Name?.Trim());
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(audioFile));
        });

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

        // GET /api/v1/audio-files/platform/{voice}/{phrase}/stream — preview a platform phrase-library
        // clip (committed OGG under freeswitch/sounds/_platform/, not tenant-scoped). Same clip the
        // "__platform:{voice}/{phrase}" flow ref resolves to. Segments restricted to [a-z0-9_].
        group.MapGet("/platform/{voice}/{phrase}/stream", (
            string voice,
            string phrase,
            IWebHostEnvironment env,
            IConfiguration config) =>
        {
            static bool Safe(string s) => s.Length is > 0 and <= 64 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
            if (!Safe(voice) || !Safe(phrase)) return Results.NotFound();

            var diskPath = Path.Combine(ResolveSoundsHostPath(config, env), "_platform", voice, $"{phrase}.ogg");
            return File.Exists(diskPath)
                ? Results.File(diskPath, "audio/ogg", $"{voice}-{phrase}.ogg")
                : Results.NotFound();
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

    private static string TenantAudioDir(IConfiguration config, IWebHostEnvironment env, string schemaName) =>
        Path.Combine(ResolveSoundsHostPath(config, env), schemaName);

    /// <summary>
    /// Transcodes to OGG Vorbis 8 kHz mono — the format FreeSWITCH mod_sndfile handles natively —
    /// shared by both the raw-upload endpoint and the TTS-synthesis endpoints below. Returns the
    /// resulting file's size in bytes; throws (with the source file already cleaned up by the
    /// caller's finally block) on any ffmpeg failure.
    /// </summary>
    private static async Task<long> TranscodeToOggAsync(
        IConfiguration config, string sourcePath, string oggPath, CancellationToken ct)
    {
        var ffmpegExe = config["FreeSWITCH:FfmpegPath"] ?? "ffmpeg";
        var psi = new ProcessStartInfo(ffmpegExe)
        {
            Arguments        = $"-y -i \"{sourcePath}\" -vn -ar 8000 -ac 1 -c:a libvorbis -q:a 3 \"{oggPath}\"",
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
        {
            if (File.Exists(oggPath)) File.Delete(oggPath);
            throw new InvalidOperationException($"ffmpeg exited with code {proc.ExitCode}. stderr: {stderr.TrimEnd()}");
        }

        return new FileInfo(oggPath).Length;
    }

    /// <summary>Writes 16-bit mono linear PCM as a standard 44-byte-header WAV — same shape as
    /// TtsFileSynthesizer's own writer, duplicated here rather than shared since it's a few lines
    /// of pure format framing with no dependency either side needs on the other.</summary>
    private static byte[] WriteWavBytes(byte[] pcm, int sampleRateHz)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        var byteRate = sampleRateHz * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8.ToArray());

        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRateHz);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write((short)bitsPerSample);

        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);

        return ms.ToArray();
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
        id               = f.Id,
        name             = f.Name,
        originalFileName = f.OriginalFileName,
        contentType      = f.ContentType,
        fileSizeBytes    = f.FileSizeBytes,
        createdAt        = f.CreatedAt,
        isTtsGenerated   = f.IsTtsGenerated,
        ttsSourceText    = f.TtsSourceText,
        ttsProviderKey   = f.TtsProviderKey,
        ttsVoiceId       = f.TtsVoiceId,
    };

    private record SaveTtsClipRequest(string? Name, string Text, string VoiceId);
}
