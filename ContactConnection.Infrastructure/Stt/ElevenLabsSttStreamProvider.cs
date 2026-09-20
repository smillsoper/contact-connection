using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Stt;

/// <summary>
/// Streams caller audio to ElevenLabs' documented realtime STT WebSocket
/// (wss://api.elevenlabs.io/v1/speech-to-text/realtime) — same direct-ClientWebSocket approach
/// as ElevenLabsTtsStreamProvider (no vendor SDK needed, ElevenLabs publishes this as a stable
/// integration surface).
///
/// Wire protocol (confirmed against ElevenLabs' API reference):
///   → {"message_type":"input_audio_chunk","audio_base_64":"...","sample_rate":8000}
///   ← {"message_type":"partial_transcript","text":"..."}       (interim)
///   ← {"message_type":"committed_transcript","text":"..."}     (final for that segment)
/// </summary>
public class ElevenLabsSttStreamProvider : ISpeechRecognitionProvider
{
    public string ProviderKey => "elevenlabs";

    public IReadOnlyList<string> RequiredCredentialFields => ["apiKey"];

    private readonly ILogger<ElevenLabsSttStreamProvider> _logger;

    public ElevenLabsSttStreamProvider(ILogger<ElevenLabsSttStreamProvider> logger) => _logger = logger;

    /// <summary>Credentials required: "apiKey". ProviderSettings (optional): "languageCode"
    /// (ISO 639-1/639-3, default unset = auto-detect).</summary>
    public async IAsyncEnumerable<SttTranscriptEvent> TranscribeAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioFrames, SttStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!request.Credentials.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs STT requires an 'apiKey' credential.");

        var audioFormat = ResolveAudioFormat(request.SampleRateHz);
        var languageCode = request.ProviderSettings?.GetValueOrDefault("languageCode");

        // commit_strategy=vad — ElevenLabs defaults to manual-commit mode, which finalizes a
        // segment only when the client sends an explicit "commit" field on an input_audio_chunk
        // message. This capture has no notion of "the caller just stopped talking" to trigger
        // that itself, so without this the vendor buffers audio and never emits a
        // committed_transcript (confirmed live S148: 996 frames forwarded, zero transcripts back,
        // session_started echoed vad_commit_strategy:false). VAD mode makes the vendor's own
        // silence detection do the committing instead — the right shape for a single short
        // spoken utterance in a bounded IVR listen window.
        var uriBuilder = new StringBuilder(
            $"wss://api.elevenlabs.io/v1/speech-to-text/realtime?audio_format={audioFormat}&commit_strategy=vad");
        if (!string.IsNullOrWhiteSpace(languageCode))
            uriBuilder.Append($"&language_code={Uri.EscapeDataString(languageCode)}");

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("xi-api-key", apiKey);
        await socket.ConnectAsync(new Uri(uriBuilder.ToString()), ct);
        _logger.LogInformation("ElevenLabs STT: connected (audioFormat={Format})", audioFormat);

        // Pump audio frames to the vendor concurrently with reading transcript messages back —
        // a captured caller utterance and its transcript both flow continuously, not
        // request/response. Frame-send failures are logged but don't end enumeration; the read
        // loop below is what actually terminates this method (vendor close, or the caller
        // cancels once it has a match).
        var framesSent = 0;
        var bytesSent = 0L;
        var sendTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in audioFrames.WithCancellation(ct))
                {
                    var msg = JsonSerializer.Serialize(new
                    {
                        message_type = "input_audio_chunk",
                        audio_base_64 = Convert.ToBase64String(frame.Span),
                        sample_rate = request.SampleRateHz,
                    });
                    await socket.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);
                    framesSent++;
                    bytesSent += frame.Length;
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "ElevenLabs STT: audio send loop ended (socket closed)");
            }
            finally
            {
                // Confirms whether caller audio ever actually reached the vendor at all —
                // zero frames here means the problem is upstream (mod_audio_stream never sent
                // us anything), not ElevenLabs or phrase matching.
                _logger.LogInformation(
                    "ElevenLabs STT: audio send loop ended — {Frames} frame(s), {Bytes} byte(s) forwarded",
                    framesSent, bytesSent);
            }
        }, ct);

        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var messageBytes = new MemoryStream();
                WebSocketReceiveResult result;
                try
                {
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        messageBytes.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    _logger.LogInformation("ElevenLabs STT: socket closed ({Msg})", ex.Message);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close) break;

                using var doc = JsonDocument.Parse(messageBytes.ToArray());
                var root = doc.RootElement;
                var messageType = root.TryGetProperty("message_type", out var mt) ? mt.GetString() : null;
                var text = root.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;

                if (string.IsNullOrEmpty(text))
                {
                    // Anything that isn't a transcript with text — an auth/format/rate-limit
                    // error, a message shape this integration doesn't know about yet, a no-
                    // speech-detected notice. Logged in full rather than silently dropped: this
                    // is exactly the kind of thing that otherwise reads as "voice recognition
                    // just doesn't work" with zero clue why.
                    _logger.LogInformation(
                        "ElevenLabs STT: non-transcript message (messageType={MessageType}): {Raw}",
                        messageType ?? "(none)", root.GetRawText());
                    continue;
                }

                switch (messageType)
                {
                    case "partial_transcript":
                        yield return new SttTranscriptEvent(text, IsFinal: false);
                        break;
                    case "committed_transcript":
                    case "committed_transcript_with_timestamps":
                        yield return new SttTranscriptEvent(text, IsFinal: true);
                        break;
                    default:
                        _logger.LogInformation(
                            "ElevenLabs STT: unrecognized message_type '{MessageType}' with text: {Raw}",
                            messageType, root.GetRawText());
                        break;
                }
            }
        }
        finally
        {
            if (socket.State == WebSocketState.Open)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                catch (WebSocketException ex)
                {
                    _logger.LogDebug(ex, "ElevenLabs STT: courtesy close failed — ignoring");
                }
            }
            await sendTask;
        }
    }

    // 8000/16000 are the two rates mod_audio_stream ever hands us (StartAudioStreamAsync's
    // "8k"/"mono" label for a PSTN leg) — pass through as-is rather than resampling, ElevenLabs
    // accepts both natively.
    private static string ResolveAudioFormat(int sampleRateHz) => sampleRateHz switch
    {
        <= 8000 => "pcm_8000",
        <= 16000 => "pcm_16000",
        <= 22050 => "pcm_22050",
        <= 24000 => "pcm_24000",
        <= 44100 => "pcm_44100",
        _ => "pcm_48000",
    };
}
