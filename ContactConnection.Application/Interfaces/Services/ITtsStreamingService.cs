namespace ContactConnection.Application.Interfaces.Services;

/// <summary>Which vendor the tenant has configured for ApiSubType.TtsStreaming, if any.</summary>
public sealed record TtsStreamingProviderInfo(string ProviderKey, string? SettingsJson);

/// <summary>
/// Shared TTS-streaming plumbing used by every node handler that can offer a live vendor voice
/// as an alternative to flite (tf_play, tf_whisper, tf_transfer's live-broadcast destinations).
/// Resolves the tenant's single configured ApiSubType.TtsStreaming preference — there is no
/// per-node vendor choice; a tenant has at most one active streaming provider at a time — and
/// hands back a <c>shout://</c> URL the caller <c>uuid_broadcast</c>s like any other media arg.
///
/// The URL points at the API's own MP3 relay endpoint (<c>/relay/tts-mp3/{token}</c>), which
/// synthesizes on demand and streams the result as one continuous MP3 over HTTP. FreeSWITCH's
/// mod_shout (libcurl → mpg123) plays it progressively, so one broadcast = one PLAYBACK_STOP =
/// gapless audio, with no per-chunk uuid_broadcast and no mod_audio_stream event plumbing.
///
/// NOT usable from a node whose remaining work runs inside a single FreeSWITCH dialplan
/// uuid_transfer (tf_voicemail's greeting, tf_transfer's external_number announcement) — those
/// have no live channel to broadcast onto mid-dialplan, so they need
/// <see cref="ITtsFileSynthesizer"/>'s pre-synthesized-file path instead.
/// </summary>
public interface ITtsStreamingService
{
    /// <summary>
    /// Looks up the tenant's chosen provider for ApiSubType.TtsStreaming, if any — either a
    /// platform-catalog PortalApiEndpoint, or the tenant's own TenantApiEndpoint (they manage
    /// their own vendor subscription/credentials rather than sharing the platform's). Null means
    /// no preference configured — callers fall back to flite.
    /// </summary>
    Task<TtsStreamingProviderInfo?> ResolveProviderAsync(string tenantSchemaName, CancellationToken ct = default);

    /// <summary>
    /// Stashes a synthesis request in Redis under a fresh correlation token and returns a
    /// <c>shout://…/relay/tts-mp3/{token}</c> URL. The caller <c>uuid_broadcast</c>s it on
    /// whichever channel it wants the speech on (caller leg or agent whisper leg); the resulting
    /// single PLAYBACK_STOP drives the flow's <c>tts_finished</c> transition. Does no ESL / no
    /// FreeSWITCH work itself.
    /// </summary>
    Task<string> PrepareStreamUrlAsync(
        string tenantSubdomain, TtsStreamingProviderInfo provider, string text, string voiceId,
        CancellationToken ct = default);
}
