namespace ContactConnection.Application.Interfaces.Services;

/// <summary>Which vendor the tenant has configured for ApiSubType.TtsStreaming, if any.</summary>
public sealed record TtsStreamingProviderInfo(string ProviderKey, string? SettingsJson);

/// <summary>
/// Shared TTS-streaming plumbing used by every node handler that can offer a live vendor voice
/// as an alternative to flite (tf_play, tf_transfer's live-broadcast destinations). Resolves the
/// tenant's single configured ApiSubType.TtsStreaming preference — there is no per-node vendor
/// choice; a tenant has at most one active streaming provider at a time, same as today — and arms
/// a live mod_audio_stream session on a channel that's still under our own ESL commands.
///
/// NOT usable from a node whose remaining work runs inside a single FreeSWITCH dialplan
/// uuid_transfer (tf_voicemail's greeting, tf_transfer's external_number announcement) — those
/// have no live ESL/event hook mid-dialplan, so they need <see cref="ITtsFileSynthesizer"/>'s
/// pre-synthesized-file path instead.
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
    /// Fire-and-forget: arms a live mod_audio_stream session on ctx.ChannelUuid via a short-lived
    /// Redis-cached correlation token. The relay (/relay/tts-stream) picks up that token, resolves
    /// the tenant's vendor credentials, and performs the actual synthesis call — this method only
    /// starts the FreeSWITCH-side stream and returns immediately; it does not wait for playback.
    /// </summary>
    Task StartStreamAsync(
        TelephonyFlowContext ctx, TtsStreamingProviderInfo provider, string text, string voiceId,
        CancellationToken ct = default);
}
