namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// The payload PlayNodeHandler stashes in the telephony session cache (Redis, short TTL,
/// single-use) before issuing "uuid_audio_stream ... start", keyed by a short correlation
/// token. That token — not this payload — is what travels over the FreeSWITCH ESL command
/// line as the "metadata" argument, keeping call text and provider identity out of
/// FreeSWITCH's own command-line logs. No credentials here: the relay re-resolves those
/// itself from ITenantCredentialStore using TenantSubdomain + ProviderKey at synthesis time.
///
/// ChannelUuid is required for a non-obvious reason: mod_audio_stream's underlying WebSocket
/// library (IXWebSocket) auto-reconnects on ANY connection close, including a normal one we
/// initiate after synthesis finishes — closing our side of the socket is not enough to stop
/// it. The relay must also issue "uuid_audio_stream {uuid} stop" over ESL (a separate control
/// channel to FreeSWITCH) to tell the module itself the stream is done; verified live —
/// without this, FreeSWITCH reconnects in a tight retry loop indefinitely.
/// </summary>
public sealed record TtsStreamRelayRequest(
    string ChannelUuid,
    string TenantSubdomain,
    string ProviderKey,
    string VoiceId,
    string Text,
    int PreferredSampleRateHz,
    IReadOnlyDictionary<string, string>? ProviderSettings);
