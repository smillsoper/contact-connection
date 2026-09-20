using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// The WebSocket server mod_audio_stream's "uuid_audio_stream ... start" connects to for
/// tf_ivr_menu's voice-recognition option (S148) — the recognition mirror of the pre-S126
/// TTS WebSocket relay (recovered via `git show 715e1af:.../TtsStreamRelayEndpoints.cs` to
/// confirm the wire protocol). FreeSWITCH sends the correlation token (via the command's
/// "metadata" argument) as the first text message; we look up the real request (stashed in
/// Redis by ISttStreamingService.PrepareCaptureAsync) keyed by that token, resolve the
/// tenant's chosen ISpeechRecognitionProvider + credentials, and forward the caller's audio
/// (binary frames) to it. Recognized phrases race a DTMF press
/// (EslBackgroundService.HandleDtmfAsync) via IIvrVoiceResolutionCoordinator — whichever
/// resolves first wins.
///
/// No bearer auth — internal-network only (FreeSWITCH container → API host), same posture as
/// FreeSwitchDirectoryEndpoints / TtsStreamRelayEndpoints.
/// </summary>
public static class SttStreamRelayEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IEndpointRouteBuilder MapSttStreamRelayEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/relay/stt-stream", Handle).AllowAnonymous();
        return app;
    }

    private static async Task Handle(
        HttpContext context,
        ISpeechRecognitionProviderFactory providerFactory,
        ITenantCredentialStore credentialStore,
        ITelephonyCallSessionStore cache,
        IIvrVoiceResolutionCoordinator coordinator,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("SttStreamRelay");

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        var token = await ReceiveTextAsync(socket, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("STT relay: connection closed with no correlation token");
            await CloseAsync(socket, "no token", ct);
            return;
        }

        var cacheKey = $"stt_relay:{token}";
        var payloadJson = await cache.GetKeyAsync(cacheKey, ct);
        if (payloadJson is null)
        {
            logger.LogWarning("STT relay: unknown or expired correlation token {Token}", token);
            await CloseAsync(socket, "unknown token", ct);
            return;
        }
        // Deliberately NOT single-use (unlike the old TTS relay's token, which really was a
        // one-shot delivery): mod_audio_stream's underlying WS library can reconnect mid-capture
        // (live-observed — a second connection with the same token arrived seconds after the
        // first), and a live multi-second audio stream needs to survive that, not get treated as
        // an expired request. Left to expire via its own TTL instead. Any capture that opens
        // more than one ElevenLabs connection this way is a little wasteful but harmless —
        // IIvrVoiceResolutionCoordinator's claim still only lets one of them actually resolve.

        SttStreamRelayRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SttStreamRelayRequest>(payloadJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "STT relay: malformed cached payload for token {Token}", token);
            await CloseAsync(socket, "bad payload", ct);
            return;
        }
        if (request is null)
        {
            await CloseAsync(socket, "bad payload", ct);
            return;
        }

        ISpeechRecognitionProvider provider;
        try
        {
            provider = providerFactory.Resolve(request.ProviderKey);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "STT relay: no provider for key {ProviderKey}", request.ProviderKey);
            await CloseAsync(socket, "no provider", ct);
            return;
        }

        var credentials = new Dictionary<string, string>();
        foreach (var field in provider.RequiredCredentialFields)
        {
            var value = await credentialStore.GetForTenantAsync(
                request.TenantSubdomain, SttCredentialKeys.For(request.ProviderKey, field), ct);
            if (value is null)
            {
                logger.LogWarning(
                    "STT relay: tenant {Tenant} has no '{Field}' credential for provider {Provider}",
                    request.TenantSubdomain, field, request.ProviderKey);
                await CloseAsync(socket, "no credentials", ct);
                return;
            }
            credentials[field] = value;
        }

        var sttRequest = new SttStreamRequest(credentials, request.SampleRateHz, request.ProviderSettings);

        string? resolveTarget = null;
        string? matchedPhrase = null;
        var heardTranscripts = new List<string>();
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(request.TimeoutMs, 1000)));
            try
            {
                await foreach (var evt in provider.TranscribeAsync(ReadBinaryFramesAsync(socket, cts.Token), sttRequest, cts.Token))
                {
                    var normalized = IvrMenu.NormalizePhrase(evt.Text);
                    logger.LogInformation(
                        "STT relay [{Uuid}]: transcript ({Kind}) '{Text}' → normalized '{Normalized}'",
                        request.ChannelUuid, evt.IsFinal ? "final" : "interim", evt.Text, normalized);

                    if (!string.IsNullOrWhiteSpace(evt.Text) && !heardTranscripts.Contains(evt.Text))
                        heardTranscripts.Add(evt.Text);

                    if (request.PhraseIndex.TryGetValue(normalized, out var matchKey)
                        && request.OptionMap.TryGetValue(matchKey, out var target))
                    {
                        logger.LogInformation(
                            "STT relay [{Uuid}]: phrase '{Text}' matched → {Target}", request.ChannelUuid, evt.Text, target);
                        resolveTarget = target;
                        matchedPhrase = evt.Text;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogInformation("STT relay [{Uuid}]: capture window elapsed with no match", request.ChannelUuid);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "STT relay [{Uuid}]: transcription failed for provider {Provider}",
                    request.ChannelUuid, request.ProviderKey);
            }
        }

        // Surfaced in the call trace (ICallTraceRecorder, via IIvrVoiceResolutionCoordinator)
        // rather than only logged — a tenant tuning phrases for this menu needs to see exactly
        // what STT transcribed without pulling API logs.
        var resolutionDetail = matchedPhrase is not null
            ? $"voice: recognized \"{matchedPhrase}\" — matched"
            : heardTranscripts.Count > 0
                ? $"voice: heard {string.Join(" / ", heardTranscripts.Select(t => $"\"{t}\""))} — no phrase match"
                : "voice: no speech detected before timeout";

        resolveTarget ??= request.NoMatchTarget;

        // A fresh, short-lived ESL connection — this relay has no live connection of its own
        // (unlike EslBackgroundService's persistent one). Same pattern the pre-S126 TTS relay
        // used for its own uuid_audio_stream stop. CancellationToken.None: this cleanup must
        // run even if the request's own `ct` is already cancelled.
        try
        {
            var host = config["FreeSWITCH:Host"] ?? "127.0.0.1";
            var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
            var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

            await using var esl = new EslClient();
            await esl.ConnectAsync(host, port, pass, CancellationToken.None);
            await coordinator.TryResolveAsync(
                request.ChannelUuid, resolveTarget, esl, resolutionDetail, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "STT relay [{Uuid}]: failed to resolve via coordinator", request.ChannelUuid);
        }

        await CloseAsync(socket, "done", CancellationToken.None);
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Yields each binary WebSocket message (the caller's raw PCM, per mod_audio_stream's
    /// protocol) as it arrives; ends on close, cancellation, or socket error.</summary>
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadBinaryFramesAsync(
        WebSocket socket, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult? result = null;
            var ended = false;

            do
            {
                try
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                }
                catch (OperationCanceledException) { ended = true; break; }
                catch (WebSocketException) { ended = true; break; }

                if (result.MessageType == WebSocketMessageType.Close) { ended = true; break; }
                if (result.MessageType == WebSocketMessageType.Binary)
                    ms.Write(buffer, 0, result.Count);
            } while (result is not null && !result.EndOfMessage);

            if (ended) yield break;
            if (ms.Length > 0) yield return ms.ToArray();
        }
    }

    private static async Task CloseAsync(WebSocket socket, string reason, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, ct);
        }
        catch { /* best-effort */ }
    }
}
