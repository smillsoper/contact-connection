using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// For every API category except Media/TtsStreaming, PortalApiDefinition.Provider is a purely
/// decorative label — the generic HTTP executor never reads it. TtsStreaming is the one place
/// it doubles as a runtime dispatch key (ITtsStreamProviderFactory.Resolve), so a typo there
/// doesn't fail at save time — it fails silently mid-call, when the relay can't resolve a
/// provider. Shared by both PortalApiDefinitionsEndpoints and PortalApiEndpointsEndpoints so a
/// bad Provider value is rejected wherever it could end up backing a TtsStreaming endpoint.
/// </summary>
public static class TtsProviderValidation
{
    /// <summary>Returns an error message if invalid, or null if provider is a registered ITtsStreamProvider key.</summary>
    public static string? Validate(string? provider, ITtsStreamProviderFactory factory)
    {
        var validKeys = factory.RegisteredProviderKeys;

        if (string.IsNullOrWhiteSpace(provider))
            return $"Provider is required for TTS Streaming endpoints. Valid values: {string.Join(", ", validKeys)}.";

        if (!validKeys.Contains(provider, StringComparer.OrdinalIgnoreCase))
            return $"Provider '{provider}' is not a registered TTS streaming provider. Valid values: {string.Join(", ", validKeys)}.";

        return null;
    }
}
