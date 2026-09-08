using System.Diagnostics;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.Http.Features;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// GET /relay/tts-mp3/{token} — the URL <see cref="ITtsStreamingService.PrepareStreamUrlAsync"/>
/// hands back (as <c>shout://…</c>). FreeSWITCH's mod_shout GETs it (libcurl → mpg123) and plays
/// it progressively into the live call.
///
/// The handler looks up the real request (stashed in Redis by the streaming service, keyed by
/// the token), resolves the tenant's chosen <see cref="ITtsStreamProvider"/> + credentials,
/// synthesizes speech as raw PCM, pipes it through ffmpeg to a single continuous CBR MP3 stream,
/// and writes that to the response body as it encodes. One HTTP response = one continuous
/// playback = one PLAYBACK_STOP — no per-chunk uuid_broadcast, no mod_audio_stream event
/// plumbing, no inter-chunk stutter.
///
/// No bearer auth — internal-network only (FreeSWITCH container → API host), same posture as
/// FreeSwitchDirectoryEndpoints.
/// </summary>
public static class TtsStreamRelayEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ffmpeg output: fixed 22.05 kHz mono CBR MP3. `-write_xing 0` drops the VBR/LAME header
    // (which would force the muxer to buffer the whole file to count frames) so ffmpeg emits a
    // pure frame stream mpg123 can sync on immediately. FreeSWITCH resamples to the call codec.
    private const string FfmpegOutArgs = "-ar 22050 -ac 1 -c:a libmp3lame -b:a 32k -write_xing 0 -flush_packets 1 -f mp3 pipe:1";

    public static IEndpointRouteBuilder MapTtsStreamRelayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/relay/tts-mp3/{token}", Handle).AllowAnonymous();
        return app;
    }

    private static async Task Handle(
        string token,
        HttpContext context,
        ITtsStreamProviderFactory providerFactory,
        ITenantCredentialStore credentialStore,
        ITelephonyCallSessionStore cache,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TtsMp3Relay");

        var cacheKey = $"tts_relay:{token}";
        var payloadJson = await cache.GetKeyAsync(cacheKey, ct);
        if (payloadJson is null)
        {
            logger.LogWarning("TTS relay: unknown or expired token {Token}", token);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        TtsStreamRelayRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TtsStreamRelayRequest>(payloadJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "TTS relay: malformed cached payload for token {Token}", token);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }
        if (request is null)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        ITtsStreamProvider provider;
        try
        {
            provider = providerFactory.Resolve(request.ProviderKey);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "TTS relay: no provider for key {ProviderKey}", request.ProviderKey);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        var credentials = new Dictionary<string, string>();
        foreach (var field in provider.RequiredCredentialFields)
        {
            var value = await credentialStore.GetForTenantAsync(
                request.TenantSubdomain, TtsCredentialKeys.For(request.ProviderKey, field), ct);
            if (value is null)
            {
                logger.LogWarning(
                    "TTS relay: tenant {Tenant} has no '{Field}' credential for provider {Provider}",
                    request.TenantSubdomain, field, request.ProviderKey);
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            credentials[field] = value;
        }

        // Lead-in silence prepended to the encoder — RTP is otherwise not flowing steadily to the
        // far end when the first real audio starts, clipping the opening syllable (same defect
        // S117 fixed on the file / flite paths). tf_answer primes once; this covers a prompt after
        // a longer gap. Config: FreeSWITCH:TtsStreamLeadInSilenceMs, 0 disables.
        var leadInMs = config.GetValue<int?>("FreeSWITCH:TtsStreamLeadInSilenceMs") ?? 300;
        var ffmpegExe = config["FreeSWITCH:FfmpegPath"] ?? "ffmpeg";

        context.Response.ContentType = "audio/mpeg";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var synthesisRequest = new TtsStreamRequest(
            request.Text, request.VoiceId, credentials, request.PreferredSampleRateHz, request.ProviderSettings);

        try
        {
            await StreamMp3Async(synthesisRequest, provider, ffmpegExe, leadInMs, context, logger, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TTS relay [{Token}]: client disconnected mid-stream", token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS relay [{Token}]: synthesis/encode failed", token);
        }
        finally
        {
            await cache.DeleteKeyAsync(cacheKey, CancellationToken.None);
        }
    }

    /// <summary>
    /// Pumps provider PCM → ffmpeg stdin and ffmpeg stdout (CBR MP3) → the HTTP response, both
    /// concurrently, so audio streams out as it's synthesized. The first chunk's actual sample
    /// rate is read before ffmpeg starts (each provider produces one rate for the whole synthesis).
    /// </summary>
    private static async Task StreamMp3Async(
        TtsStreamRequest request,
        ITtsStreamProvider provider,
        string ffmpegExe,
        int leadInMs,
        HttpContext context,
        ILogger logger,
        CancellationToken ct)
    {
        await using var enumerator = provider.SynthesizeAsync(request, ct).GetAsyncEnumerator(ct);

        if (!await enumerator.MoveNextAsync())
        {
            logger.LogWarning("TTS relay: provider yielded no audio");
            return;
        }

        var firstChunk = enumerator.Current;
        var rate = firstChunk.SampleRateHz;

        var psi = new ProcessStartInfo(ffmpegExe)
        {
            Arguments              = $"-hide_banner -loglevel error -f s16le -ar {rate} -ac 1 -i pipe:0 {FfmpegOutArgs}",
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "ffmpeg could not be started. Ensure it is installed and on PATH (or set FreeSWITCH:FfmpegPath).");

        var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);

        // If the client (mod_shout) drops mid-stream, kill ffmpeg so it doesn't linger.
        using var killOnAbort = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        var pumpIn = Task.Run(async () =>
        {
            try
            {
                var stdin = proc.StandardInput.BaseStream;
                if (leadInMs > 0)
                {
                    var silence = new byte[leadInMs * rate / 1000 * 2];
                    await stdin.WriteAsync(silence, ct);
                }
                await stdin.WriteAsync(firstChunk.Data, ct);
                while (await enumerator.MoveNextAsync())
                    await stdin.WriteAsync(enumerator.Current.Data, ct);
                await stdin.FlushAsync(ct);
            }
            finally
            {
                proc.StandardInput.Close(); // EOF → ffmpeg flushes trailing MP3 frames and exits
            }
        }, ct);

        var pumpOut = Task.Run(async () =>
        {
            var buffer = new byte[8 * 1024];
            var stdout = proc.StandardOutput.BaseStream;
            int read;
            while ((read = await stdout.ReadAsync(buffer, ct)) > 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }, ct);

        await Task.WhenAll(pumpIn, pumpOut);
        await proc.WaitForExitAsync(CancellationToken.None);

        if (proc.ExitCode != 0 && !ct.IsCancellationRequested)
            logger.LogWarning("TTS relay: ffmpeg exited {Code}: {Stderr}", proc.ExitCode, (await stderrTask).Trim());
    }
}
