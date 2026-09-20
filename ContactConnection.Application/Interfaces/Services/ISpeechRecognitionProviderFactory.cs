namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Resolves the correct ISpeechRecognitionProvider implementation for a given provider key.
/// Registered as a singleton; providers register themselves by ProviderKey. Mirrors
/// ITtsStreamProviderFactory exactly.
/// </summary>
public interface ISpeechRecognitionProviderFactory
{
    /// <summary>
    /// Returns the ISpeechRecognitionProvider for the given key. No default/fallback — a tenant
    /// with no TenantApiPreference for SttStreaming simply isn't offered voice recognition at
    /// all (IvrMenuNodeHandler falls back to its digits-only path). Throws
    /// InvalidOperationException if providerKey doesn't match a registered provider.
    /// </summary>
    ISpeechRecognitionProvider Resolve(string providerKey);

    /// <summary>All currently registered provider keys — single source of truth for validating
    /// PortalApiDefinition.Provider and for an admin provider picker, same role
    /// ITtsStreamProviderFactory.RegisteredProviderKeys plays for TTS.</summary>
    IReadOnlyCollection<string> RegisteredProviderKeys { get; }
}
