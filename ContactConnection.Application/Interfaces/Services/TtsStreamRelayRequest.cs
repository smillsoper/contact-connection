namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// The payload <see cref="ITtsStreamingService"/> stashes in the telephony session cache (Redis,
/// short TTL, single-use) before returning a <c>shout://</c> URL, keyed by a short correlation
/// token. That token — not this payload — is what travels in the URL and over FreeSWITCH's ESL
/// command line, keeping call text and provider identity out of FreeSWITCH's own logs. No
/// credentials here: the MP3 relay endpoint re-resolves those itself from
/// <c>ITenantCredentialStore</c> using <see cref="TenantSubdomain"/> + <see cref="ProviderKey"/>
/// at synthesis time.
/// </summary>
public sealed record TtsStreamRelayRequest(
    string TenantSubdomain,
    string ProviderKey,
    string VoiceId,
    string Text,
    int PreferredSampleRateHz,
    IReadOnlyDictionary<string, string>? ProviderSettings);
