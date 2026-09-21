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
///
/// FreeForm (tf_data_collect — DataCollectNodeHandler) switches SttStreamRelayEndpoints into a
/// different mode entirely: instead of matching interim/final transcripts against PhraseIndex/
/// OptionMap, it waits for the first non-empty FINAL transcript and hands that verbatim text to
/// IDataCollectResolutionCoordinator, which decides where to route (it owns the "collected"/
/// "timeout" targets and the variable name, read from the node's own session vars — the relay
/// doesn't need to know any of that). PhraseIndex/OptionMap/NoMatchTarget are unused when true.
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
    int SampleRateHz,
    bool FreeForm = false);
