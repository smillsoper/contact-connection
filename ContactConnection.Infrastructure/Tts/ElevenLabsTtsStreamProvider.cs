using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Tts;

/// <summary>
/// Streams TTS via ElevenLabs' documented WebSocket protocol (wss://api.elevenlabs.io/v1/
/// text-to-speech/{voice_id}/stream-input) — unlike Azure, ElevenLabs publishes this as a
/// stable third-party integration surface, so a direct ClientWebSocket implementation is
/// appropriate here (no vendor SDK dependency needed).
/// </summary>
public class ElevenLabsTtsStreamProvider : ITtsStreamProvider
{
    public string ProviderKey => "elevenlabs";

    public IReadOnlyList<string> RequiredCredentialFields => ["apiKey"];

    private readonly ILogger<ElevenLabsTtsStreamProvider> _logger;

    public ElevenLabsTtsStreamProvider(ILogger<ElevenLabsTtsStreamProvider> logger) => _logger = logger;

    /// <summary>
    /// Credentials required: "apiKey". ProviderSettings (all optional): "modelId" (default
    /// "eleven_flash_v2_5" — their lowest-latency model), "stability", "similarityBoost",
    /// "style", "speed" (ElevenLabs voice_settings, parsed as doubles; ElevenLabs' own
    /// defaults are used for anything unset).
    /// </summary>
    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(
        TtsStreamRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!request.Credentials.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs TTS requires an 'apiKey' credential.");

        var modelId = request.ProviderSettings?.GetValueOrDefault("modelId") ?? "eleven_flash_v2_5";
        var (outputFormat, sampleRateHz) = ResolveOutputFormat(request.PreferredSampleRateHz);

        var uri = new Uri(
            $"wss://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}/stream-input" +
            $"?model_id={Uri.EscapeDataString(modelId)}&output_format={outputFormat}");

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("xi-api-key", apiKey);
        await socket.ConnectAsync(uri, ct);
        _logger.LogInformation(
            "ElevenLabs TTS: connected (voice={Voice} model={Model} outputFormat={Format})",
            request.VoiceId, modelId, outputFormat);

        // Init message opens the generation context; voice_settings apply for the whole
        // connection. The text field here is a required placeholder, not spoken content.
        await SendAsync(socket, JsonSerializer.Serialize(new
        {
            text = " ",
            voice_settings = BuildVoiceSettings(request.ProviderSettings),
        }), ct);

        // We already have the complete text (not a live LLM token stream), so it's sent as
        // one message with flush=true to skip ElevenLabs' chunk-buffering heuristics — those
        // exist to smooth prosody across incrementally-arriving text, which doesn't apply here
        // and would only add latency. Per protocol, text must end with a trailing space.
        await SendAsync(socket, JsonSerializer.Serialize(new
        {
            text = request.Text.TrimEnd() + " ",
            flush = true,
        }), ct);

        // Empty text signals end of input — ElevenLabs closes the stream after the final chunk.
        await SendAsync(socket, JsonSerializer.Serialize(new { text = "" }), ct);

        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            using var messageBytes = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                messageBytes.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation(
                    "ElevenLabs TTS: server closed the socket (status={Status} description={Description})",
                    socket.CloseStatus, socket.CloseStatusDescription);
                break;
            }

            using var doc = JsonDocument.Parse(messageBytes.ToArray());
            var root = doc.RootElement;

            var hasAudio = root.TryGetProperty("audio", out var audioEl) && audioEl.ValueKind == JsonValueKind.String;
            var audioLen = hasAudio ? audioEl.GetString()!.Length : 0;
            var isFinal = root.TryGetProperty("isFinal", out var finalEl) && finalEl.ValueKind == JsonValueKind.True;

            // Diagnostic — every reply's shape, not just error frames. audio is logged as its
            // base64 length only (never the content): confirms whether ElevenLabs is sending real
            // audio at all vs. e.g. an empty/zero-length "audio" field, an unrecognized message
            // shape, or a close with no payload — narrows "silence on the call" down to our
            // parsing vs. the vendor vs. the mod_audio_stream/FreeSWITCH playback side.
            _logger.LogInformation(
                "ElevenLabs TTS message: hasAudio={HasAudio} audioB64Len={AudioLen} isFinal={IsFinal} keys=[{Keys}]",
                hasAudio, audioLen, isFinal, string.Join(",", root.EnumerateObject().Select(p => p.Name)));

            if (hasAudio)
            {
                var audioBytes = Convert.FromBase64String(audioEl.GetString()!);
                if (audioBytes.Length > 0)
                    yield return new TtsAudioChunk(audioBytes, sampleRateHz);
            }
            else if (root.TryGetProperty("error", out var errorEl))
            {
                _logger.LogWarning("ElevenLabs TTS error message: {Error}", errorEl.ToString());
            }

            if (isFinal)
                break;
        }

        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    private static object BuildVoiceSettings(IReadOnlyDictionary<string, string>? settings) => new
    {
        stability = ParseDouble(settings, "stability", 0.5),
        similarity_boost = ParseDouble(settings, "similarityBoost", 0.75),
        style = ParseDouble(settings, "style", 0.0),
        use_speaker_boost = true,
        speed = ParseDouble(settings, "speed", 1.0),
    };

    private static double ParseDouble(IReadOnlyDictionary<string, string>? settings, string key, double fallback) =>
        settings is not null
        && settings.TryGetValue(key, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static Task SendAsync(ClientWebSocket socket, string json, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);

    // ElevenLabs' PCM options are 16000/22050/24000/44100 — no 8000, unlike Azure. Nearest-
    // rate-up mapping; mod_audio_stream resamples to the channel's actual codec regardless.
    private static (string OutputFormat, int SampleRateHz) ResolveOutputFormat(int preferredSampleRateHz) =>
        preferredSampleRateHz switch
        {
            <= 16000 => ("pcm_16000", 16000),
            <= 22050 => ("pcm_22050", 22050),
            <= 24000 => ("pcm_24000", 24000),
            _        => ("pcm_44100", 44100),
        };
}
