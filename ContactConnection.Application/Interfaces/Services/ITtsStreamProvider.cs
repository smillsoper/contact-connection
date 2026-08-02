namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Streams synthesized speech from an external TTS vendor (Azure, ElevenLabs, etc.) for
/// injection into a live call via mod_audio_stream — the alternative to the built-in
/// flite path in PlayNodeHandler, selected per-tenant when a TenantApiPreference exists
/// for ApiSubType.TtsStreaming.
///
/// Deliberately vendor-agnostic: this interface knows nothing about mod_audio_stream's
/// wire protocol (WebSocket, JSON envelope, base64 audioData). That translation is a
/// separate relay component's job, so these providers stay reusable for anything else
/// that wants synthesized speech (e.g. pre-generating cached announcement files) without
/// dragging FreeSWITCH-specific framing into vendor-adapter code.
///
/// New providers are registered in DI as named implementations via ITtsStreamProviderFactory.
/// Adding a new vendor never requires changes to the relay component or PlayNodeHandler.
/// </summary>
public interface ITtsStreamProvider
{
    /// <summary>
    /// The provider identifier that selects this implementation (e.g. "azure", "elevenlabs").
    /// Matches the Provider value on the tenant's registered PortalApiDefinition/TenantApiEndpoint
    /// for ApiSubType.TtsStreaming.
    /// </summary>
    string ProviderKey { get; }

    /// <summary>
    /// Names of the credential fields this provider needs (e.g. Azure: ["apiKey", "region"];
    /// ElevenLabs: ["apiKey"]). The orchestration layer that builds TtsStreamRequest.Credentials
    /// resolves each via TtsCredentialKeys.For(ProviderKey, field) against ITenantCredentialStore
    /// — providers never touch the credential store directly, and this list is what lets that
    /// resolution be generic instead of a per-provider if/else chain. Provider-prefixed key
    /// names matter: ITenantCredentialStore is a flat per-tenant store, so an unprefixed "apiKey"
    /// would collide between vendors if a tenant configured more than one.
    /// </summary>
    IReadOnlyList<string> RequiredCredentialFields { get; }

    /// <summary>
    /// Synthesizes speech and yields raw PCM audio chunks as they arrive from the vendor —
    /// not buffered to a complete file first. Implementations should request raw linear PCM
    /// directly from the vendor's streaming endpoint where the vendor's API supports it
    /// (avoiding an extra decode step); where it doesn't (e.g. a vendor that only streams
    /// MP3/Opus), the implementation decodes internally so every provider yields PCM
    /// regardless of what the vendor natively speaks.
    ///
    /// TtsStreamRequest.PreferredSampleRateHz is a hint, not a contract — vendors don't all
    /// support the same rate set (e.g. ElevenLabs' PCM options are 16000/22050/24000/44100,
    /// no 8000), so each implementation picks its nearest supported native rate and reports
    /// the ACTUAL rate on every yielded chunk. mod_audio_stream resamples on the FreeSWITCH
    /// side to match the channel's real codec regardless, so precision here just avoids an
    /// unnecessary resample in application code — never lie about the rate you produced.
    /// </summary>
    IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(TtsStreamRequest request, CancellationToken ct = default);
}

/// <summary>A chunk of raw 16-bit mono linear PCM at SampleRateHz.</summary>
public readonly record struct TtsAudioChunk(ReadOnlyMemory<byte> Data, int SampleRateHz);

/// <summary>
/// Credentials and ProviderSettings are kept as separate bags rather than one blob: Credentials
/// is sourced from the Redis-cached tenant credential store and should never be logged;
/// ProviderSettings is the tenant's non-secret "mapping tab" config (voice id, style, stability,
/// region — whatever a given vendor exposes) and is safe to log for debugging. Different vendors
/// need different numbers/shapes of credential fields (ElevenLabs: one API key; Azure: key +
/// region; AWS-style: access key + secret + region), so both are free-form dictionaries rather
/// than fixed fields — each provider implementation reads the keys it expects and documents them.
/// </summary>
public sealed record TtsStreamRequest(
    string Text,
    string VoiceId,
    IReadOnlyDictionary<string, string> Credentials,
    int PreferredSampleRateHz = 8000,
    IReadOnlyDictionary<string, string>? ProviderSettings = null);
