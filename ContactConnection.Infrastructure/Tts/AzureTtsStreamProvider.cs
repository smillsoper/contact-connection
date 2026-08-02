using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Tts;

/// <summary>
/// Streams TTS via the official Azure Speech SDK rather than hand-rolling Azure's WebSocket
/// wire protocol — that protocol isn't publicly documented as a stable contract the way
/// ElevenLabs' is, so the SDK is the reliable integration point (it also handles auth,
/// reconnection, and connection pooling for us).
/// </summary>
public class AzureTtsStreamProvider : ITtsStreamProvider
{
    public string ProviderKey => "azure";

    public IReadOnlyList<string> RequiredCredentialFields => ["apiKey", "region"];

    private readonly ILogger<AzureTtsStreamProvider> _logger;

    public AzureTtsStreamProvider(ILogger<AzureTtsStreamProvider> logger) => _logger = logger;

    /// <summary>
    /// Credentials required: "apiKey" (subscription key), "region" (e.g. "eastus" — Azure
    /// Speech resources are region-scoped and there's no default to fall back to).
    /// </summary>
    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(
        TtsStreamRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!request.Credentials.TryGetValue("apiKey", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Azure TTS requires an 'apiKey' credential.");
        if (!request.Credentials.TryGetValue("region", out var region) || string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException("Azure TTS requires a 'region' credential (e.g. \"eastus\").");

        var (outputFormat, sampleRateHz) = ResolveOutputFormat(request.PreferredSampleRateHz);

        var speechConfig = SpeechConfig.FromSubscription(apiKey, region);
        speechConfig.SetSpeechSynthesisOutputFormat(outputFormat);
        speechConfig.SpeechSynthesisVoiceName = request.VoiceId;

        using var synthesizer = new SpeechSynthesizer(speechConfig, null);

        // Bridges the SDK's event-based streaming (Synthesizing fires once per audio chunk —
        // confirmed against Microsoft's own sample code, e.Result.AudioData is the per-event
        // delta, not a cumulative buffer) into IAsyncEnumerable via a channel.
        var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

        synthesizer.Synthesizing += (_, e) =>
        {
            var data = e.Result.AudioData;
            if (data is { Length: > 0 })
                channel.Writer.TryWrite(data);
        };
        synthesizer.SynthesisCompleted += (_, _) => channel.Writer.TryComplete();
        synthesizer.SynthesisCanceled += (_, e) =>
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(e.Result);
            _logger.LogWarning(
                "Azure TTS synthesis canceled: {Reason} {ErrorCode} {ErrorDetails}",
                details.Reason, details.ErrorCode, details.ErrorDetails);
            channel.Writer.TryComplete(new InvalidOperationException(
                $"Azure TTS synthesis canceled: {details.Reason} ({details.ErrorCode}) {details.ErrorDetails}"));
        };

        var speakTask = synthesizer.SpeakTextAsync(request.Text);
        // SpeakTextAsync's own faults (auth failure, network error before any chunk arrives)
        // wouldn't otherwise reach the channel — without this, await foreach below would just
        // hang waiting on a channel nothing will ever complete.
        _ = speakTask.ContinueWith(
            t => channel.Writer.TryComplete(t.Exception),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        await foreach (var data in channel.Reader.ReadAllAsync(ct))
            yield return new TtsAudioChunk(data, sampleRateHz);

        await speakTask; // rethrow if SpeakTextAsync itself faulted after the channel closed
    }

    private static (SpeechSynthesisOutputFormat Format, int SampleRateHz) ResolveOutputFormat(int preferredSampleRateHz) =>
        preferredSampleRateHz switch
        {
            <= 8000  => (SpeechSynthesisOutputFormat.Raw8Khz16BitMonoPcm, 8000),
            <= 16000 => (SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm, 16000),
            _        => (SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm, 24000),
        };
}
