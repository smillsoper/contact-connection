namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Composes the ITenantCredentialStore key name for a TTS provider's credential field.
/// Single source of truth for this naming — used by whatever resolves TtsStreamRequest.
/// Credentials (the relay/orchestration layer) and by the tenant-facing credentials UI that
/// captures these values, so the two can never drift apart on naming.
/// </summary>
public static class TtsCredentialKeys
{
    /// <summary>e.g. For("azure", "apiKey") -> "tts_azure_apiKey".</summary>
    public static string For(string providerKey, string field) => $"tts_{providerKey}_{field}";
}
