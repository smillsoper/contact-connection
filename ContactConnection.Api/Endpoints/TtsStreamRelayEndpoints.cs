using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// The WebSocket server mod_audio_stream's "uuid_audio_stream ... start" connects to.
/// FreeSWITCH sends the correlation token (via the command's "metadata" argument) as the
/// first text message; we look up the real request (stashed in Redis by PlayNodeHandler)
/// keyed by that token, resolve the tenant's chosen ITtsStreamProvider and credentials, and
/// stream synthesized audio back as mod_audio_stream's "streamAudio" JSON frames. Caller
/// audio arriving as binary frames is drained and discarded — this path is TTS-out only.
///
/// No bearer auth — internal-network only (FreeSWITCH container → API host), same posture
/// as FreeSwitchDirectoryEndpoints.
/// </summary>
public static class TtsStreamRelayEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IEndpointRouteBuilder MapTtsStreamRelayEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/relay/tts-stream", Handle).AllowAnonymous();
        return app;
    }

    private static async Task Handle(
        HttpContext context,
        ITtsStreamProviderFactory providerFactory,
        ITenantCredentialStore credentialStore,
        ITelephonyCallSessionStore cache,
        IConfiguration config,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TtsStreamRelay");

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        var token = await ReceiveTextAsync(socket, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("TTS relay: connection closed with no correlation token");
            await CloseAsync(socket, "no token", ct);
            return;
        }

        var cacheKey = $"tts_relay:{token}";
        var payloadJson = await cache.GetKeyAsync(cacheKey, ct);
        if (payloadJson is null)
        {
            logger.LogWarning("TTS relay: unknown or expired correlation token {Token}", token);
            await CloseAsync(socket, "unknown token", ct);
            return;
        }
        await cache.DeleteKeyAsync(cacheKey, ct); // single-use

        TtsStreamRelayRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TtsStreamRelayRequest>(payloadJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "TTS relay: malformed cached payload for token {Token}", token);
            await CloseAsync(socket, "bad payload", ct);
            return;
        }
        if (request is null)
        {
            await CloseAsync(socket, "bad payload", ct);
            return;
        }

        // We never need caller audio for TTS-out, but must keep draining incoming frames or
        // the connection can stall once the OS receive buffer fills.
        _ = DrainIncomingAsync(socket, logger, ct);

        try
        {
            await RunSynthesisAsync(request, socket, providerFactory, credentialStore, logger, ct);
        }
        finally
        {
            // Verified live: closing only our side of the WebSocket is not enough. mod_audio_
            // stream's underlying WebSocket library auto-reconnects on any close, including a
            // normal one we initiate — without this ESL-side stop, FreeSWITCH retries the
            // connection in a tight loop indefinitely after every synthesis, successful or not.
            //
            // CancellationToken.None here deliberately — this is cleanup that must run even
            // when the request's own `ct` is already cancelled (e.g. the WS connection was torn
            // down abruptly). Passing `ct` through was the actual bug behind the reconnect storm:
            // it made the ESL connect throw immediately, silently skipping the stop command.
            await StopFreeswitchStreamAsync(request.ChannelUuid, config, logger, CancellationToken.None);
            await CloseAsync(socket, "done", CancellationToken.None);
        }
    }

    private static async Task RunSynthesisAsync(
        TtsStreamRelayRequest request,
        WebSocket socket,
        ITtsStreamProviderFactory providerFactory,
        ITenantCredentialStore credentialStore,
        ILogger logger,
        CancellationToken ct)
    {
        ITtsStreamProvider provider;
        try
        {
            provider = providerFactory.Resolve(request.ProviderKey);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "TTS relay: no provider for key {ProviderKey}", request.ProviderKey);
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
                    "TTS relay: tenant {Tenant} has no '{Field}' credential configured for provider {Provider}",
                    request.TenantSubdomain, field, request.ProviderKey);
                return;
            }
            credentials[field] = value;
        }

        var synthesisRequest = new TtsStreamRequest(
            request.Text, request.VoiceId, credentials, request.PreferredSampleRateHz, request.ProviderSettings);

        try
        {
            await foreach (var chunk in provider.SynthesizeAsync(synthesisRequest, ct))
            {
                var frame = JsonSerializer.Serialize(new
                {
                    type = "streamAudio",
                    data = new
                    {
                        audioDataType = "raw",
                        sampleRate = chunk.SampleRateHz,
                        audioData = Convert.ToBase64String(chunk.Data.Span),
                    },
                });
                await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "TTS relay: synthesis failed for tenant {Tenant} provider {Provider}",
                request.TenantSubdomain, request.ProviderKey);
        }
    }

    private static async Task StopFreeswitchStreamAsync(string channelUuid, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        try
        {
            var host = config["FreeSWITCH:Host"] ?? "127.0.0.1";
            var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
            var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

            await using var esl = new EslClient();
            await esl.ConnectAsync(host, port, pass, ct);
            await esl.StopAudioStreamAsync(channelUuid, ct);
            logger.LogInformation("TTS relay: sent uuid_audio_stream stop for channel {Uuid}", channelUuid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS relay: failed to send uuid_audio_stream stop for channel {Uuid}", channelUuid);
        }
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

    /// <summary>Keeps reading and discarding frames so the socket never stalls; exits on close/error.</summary>
    private static async Task DrainIncomingAsync(WebSocket socket, ILogger logger, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { } // socket closed from the send side concurrently — expected
        catch (Exception ex)
        {
            logger.LogDebug(ex, "TTS relay: drain loop ended");
        }
    }

    private static async Task CloseAsync(WebSocket socket, string reason, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
        }
        catch { /* best-effort */ }
    }
}
