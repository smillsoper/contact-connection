namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// One-shot TTS synthesis to a cached WAV file, for nodes whose remaining work runs inside a
/// single FreeSWITCH dialplan uuid_transfer (tf_voicemail's greeting, tf_transfer's
/// external_number announcement) — no live ESL/event hook is available mid-dialplan, so the live
/// mod_audio_stream path (<see cref="ITtsStreamingService"/>, used by tf_play and tf_transfer's
/// other destinations) can't be inlined there. Results are cached by content hash
/// (provider+voice+text) under the tenant's sounds directory, so an unchanged greeting/
/// announcement is synthesized once — not re-billed to the vendor on every call that plays it.
/// </summary>
public interface ITtsFileSynthesizer
{
    /// <summary>
    /// Returns a FreeSWITCH-playable media arg — same shape TelephonyAudioResolver returns for an
    /// uploaded audio file — or null if the provider/credentials are missing or synthesis failed.
    /// Callers should fall back to flite on null.
    /// </summary>
    Task<string?> SynthesizeToFileAsync(
        string tenantSchemaName, string tenantSubdomain,
        string providerKey, string? providerSettingsJson, string voiceId, string text,
        CancellationToken ct = default);
}
