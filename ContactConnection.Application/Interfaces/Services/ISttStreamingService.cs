namespace ContactConnection.Application.Interfaces.Services;

/// <summary>Which vendor the tenant has configured for ApiSubType.SttStreaming, if any.</summary>
public sealed record SttStreamingProviderInfo(string ProviderKey, string? SettingsJson);

/// <summary>
/// Shared STT-streaming plumbing for tf_ivr_menu's voice-recognition option — the recognition
/// mirror of ITtsStreamingService. Resolves the tenant's single configured
/// ApiSubType.SttStreaming preference (there is no per-node vendor choice, same rule TTS
/// follows) and stashes a capture request for SttStreamRelayEndpoints to pick up once
/// FreeSWITCH's mod_audio_stream connects.
/// </summary>
public interface ISttStreamingService
{
    /// <summary>
    /// Looks up the tenant's chosen provider for ApiSubType.SttStreaming, if any — either a
    /// platform-catalog PortalApiEndpoint or the tenant's own TenantApiEndpoint. Null means no
    /// preference configured — callers fall back to DTMF-only.
    /// </summary>
    Task<SttStreamingProviderInfo?> ResolveProviderAsync(string tenantSchemaName, CancellationToken ct = default);

    /// <summary>
    /// Stashes a capture request in Redis under a fresh correlation token and returns that
    /// token. IvrMenuNodeHandler passes it as StartAudioStreamAsync's "metadata" argument —
    /// FreeSWITCH sends it back as the WebSocket relay's first text message, exactly like the
    /// TTS relay's token handshake. Does no ESL work itself.
    ///
    /// phraseIndex maps a normalized phrase to a match key; optionMap maps that same match key
    /// (or a pressed digit) to the target node id — the relay resolves a recognized phrase
    /// through both in sequence, the same two-step lookup IvrMenu.ResolveTarget does for DTMF.
    ///
    /// sampleRateHz is what SttStreamRelayEndpoints tells the STT vendor the captured audio's
    /// rate actually is — it must be the channel's real rate, NOT the label passed to
    /// StartAudioStreamAsync's sampling-rate argument. Live-verification (S149) proved
    /// mod_audio_stream's "8k"/"16k" label doesn't actually resample in this build — it delivers
    /// native audio unchanged. IvrMenuNodeHandler derives the true rate from the channel's
    /// negotiated codec NAME (ResolveSampleRateForCodec), not the "read_rate" channel var —
    /// read_rate proved unreliable by ear (reported 8000 on a call independently confirmed to be
    /// real 48kHz Opus audio). Codec name → rate is fixed by RFC, not something FreeSWITCH can
    /// misreport. A hardcoded single assumption here (8000, then a fixed 16000, then trusting
    /// read_rate) is what caused a live empty/garbled-transcript bug each time — always derive
    /// the truth per-call from the codec name instead of assuming or trusting a channel var.
    ///
    /// freeForm (tf_data_collect) — when true, phraseIndex/optionMap/noMatchTarget are ignored;
    /// pass empty dictionaries and null. See SttStreamRelayRequest.FreeForm for what this changes
    /// in SttStreamRelayEndpoints.
    /// </summary>
    Task<string> PrepareCaptureAsync(
        string channelUuid, string tenantSubdomain, SttStreamingProviderInfo provider,
        IReadOnlyDictionary<string, string> phraseIndex, IReadOnlyDictionary<string, string> optionMap,
        string? noMatchTarget, int timeoutMs, int sampleRateHz, bool freeForm = false, CancellationToken ct = default);
}
