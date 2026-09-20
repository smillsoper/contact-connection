namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// The payload ISttStreamingService stashes in the telephony session cache (Redis, short TTL,
/// single-use) before returning a correlation token, mirroring TtsStreamRelayRequest. No
/// credentials here — SttStreamRelayEndpoints re-resolves those itself from ICredentialStore
/// using TenantSubdomain + ProviderKey once FreeSWITCH's WebSocket connects.
///
/// PhraseIndex maps a normalized phrase (IvrMenu.NormalizePhrase) to the same "match key" a
/// digit press for that option would produce — see IvrMenuNodeHandler. NoMatchTarget is the
/// node to resume at if nothing matches before the capture window ends.
/// </summary>
public sealed record SttStreamRelayRequest(
    string ChannelUuid,
    string TenantSubdomain,
    string ProviderKey,
    IReadOnlyDictionary<string, string> PhraseIndex,
    IReadOnlyDictionary<string, string> OptionMap,
    string? NoMatchTarget,
    int TimeoutMs,
    IReadOnlyDictionary<string, string>? ProviderSettings,
    int SampleRateHz);
