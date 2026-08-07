namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Resolves the correct ITtsStreamProvider implementation for a given provider key.
/// Registered as a singleton; providers register themselves by ProviderKey.
/// </summary>
public interface ITtsStreamProviderFactory
{
    /// <summary>
    /// Returns the ITtsStreamProvider for the given key. Unlike ITaxProviderFactory (which
    /// falls back to a flat-rate default when no key is configured), there's no equivalent
    /// default vendor here — a tenant with no TenantApiPreference for TtsStreaming simply
    /// isn't using this path at all (PlayNodeHandler's existing flite branch handles them).
    /// Throws InvalidOperationException if providerKey doesn't match a registered provider.
    /// </summary>
    ITtsStreamProvider Resolve(string providerKey);

    /// <summary>
    /// All currently registered provider keys (e.g. "azure", "elevenlabs") — the single source
    /// of truth for validating PortalApiDefinition.Provider against and for the admin UI's
    /// provider picker (GET /api/v1/portal/tts-providers), so both stay in sync with whatever
    /// ITtsStreamProvider implementations are actually registered in DI without hand-maintaining
    /// a second list anywhere.
    /// </summary>
    IReadOnlyCollection<string> RegisteredProviderKeys { get; }
}
