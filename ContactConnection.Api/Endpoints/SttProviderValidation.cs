using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Recognition mirror of TtsProviderValidation (S148) — SttStreaming is the one place
/// PortalApiDefinition.Provider doubles as a runtime dispatch key
/// (ISpeechRecognitionProviderFactory.Resolve), so a typo there doesn't fail at save time — it
/// fails silently mid-call, when the relay can't resolve a provider. Shared by both
/// PortalApiDefinitionsEndpoints/PortalApiEndpointsEndpoints and their Admin (tenant-BYO)
/// counterparts so a bad Provider value is rejected wherever it could end up backing an
/// SttStreaming endpoint.
/// </summary>
public static class SttProviderValidation
{
    /// <summary>Returns an error message if invalid, or null if provider is a registered
    /// ISpeechRecognitionProvider key.</summary>
    public static string? Validate(string? provider, ISpeechRecognitionProviderFactory factory)
    {
        var validKeys = factory.RegisteredProviderKeys;

        if (string.IsNullOrWhiteSpace(provider))
            return $"Provider is required for STT Streaming endpoints. Valid values: {string.Join(", ", validKeys)}.";

        if (!validKeys.Contains(provider, StringComparer.OrdinalIgnoreCase))
            return $"Provider '{provider}' is not a registered STT streaming provider. Valid values: {string.Join(", ", validKeys)}.";

        return null;
    }
}
