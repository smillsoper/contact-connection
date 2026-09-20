namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Streams caller audio to an external STT vendor (Azure, ElevenLabs, etc.) and yields
/// transcript events as they arrive — the recognition-side mirror of
/// <see cref="ITtsStreamProvider"/>. Recognition compute runs on the tenant's own vendor
/// subscription (per-tenant cost/scale isolation), not on the shared FreeSWITCH host —
/// see tf_ivr_menu's voice-recognition option (S148).
///
/// New providers are registered in DI as named implementations via
/// ISpeechRecognitionProviderFactory, exactly like ITtsStreamProvider.
/// </summary>
public interface ISpeechRecognitionProvider
{
    /// <summary>The provider identifier that selects this implementation (e.g. "elevenlabs").
    /// Matches the Provider value on the tenant's registered PortalApiDefinition/
    /// TenantApiEndpoint for ApiSubType.SttStreaming.</summary>
    string ProviderKey { get; }

    /// <summary>Names of the credential fields this provider needs — resolved the same way
    /// ITtsStreamProvider.RequiredCredentialFields is, via SttCredentialKeys.For.</summary>
    IReadOnlyList<string> RequiredCredentialFields { get; }

    /// <summary>
    /// Consumes <paramref name="audioFrames"/> (raw PCM chunks as they arrive from the caller,
    /// via the mod_audio_stream relay) and yields a transcript event for each interim/final
    /// segment the vendor reports. The caller (SttStreamRelayEndpoints) is responsible for
    /// stopping enumeration once it has what it needs (a phrase match, or its own overall
    /// timeout) — implementations should honor cancellation promptly since the underlying
    /// audio source stops producing frames the moment the relay's WebSocket closes.
    /// </summary>
    IAsyncEnumerable<SttTranscriptEvent> TranscribeAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioFrames, SttStreamRequest request, CancellationToken ct = default);
}

/// <summary>One transcript segment. IsFinal distinguishes a vendor's "committed" (stable) result
/// from an interim/partial one that may still change.</summary>
public readonly record struct SttTranscriptEvent(string Text, bool IsFinal);

/// <summary>
/// Credentials and ProviderSettings mirror TtsStreamRequest's shape for the same reasons —
/// separate bags so Credentials (from the Redis-cached tenant credential store) never gets
/// logged, while ProviderSettings (language, model choice, etc.) is safe to log.
/// SampleRateHz defaults to 8000 — the rate mod_audio_stream captures at for a PSTN leg
/// (StartAudioStreamAsync's "8k" sampleRateLabel); unlike TTS output there's no benefit to
/// requesting higher for a narrowband caller leg.
/// </summary>
public sealed record SttStreamRequest(
    IReadOnlyDictionary<string, string> Credentials,
    int SampleRateHz = 8000,
    IReadOnlyDictionary<string, string>? ProviderSettings = null);
