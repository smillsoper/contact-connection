namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Composes the ICredentialStore key name for an STT provider's credential field. Mirrors
/// TtsCredentialKeys exactly, kept as a separate namespace (stt_ vs tts_) rather than shared —
/// a tenant may reasonably use a different vendor/key for recognition than for synthesis, even
/// when both happen to be "elevenlabs".
/// </summary>
public static class SttCredentialKeys
{
    /// <summary>e.g. For("elevenlabs", "apiKey") -> "stt_elevenlabs_apiKey".</summary>
    public static string For(string providerKey, string field) => $"stt_{providerKey}_{field}";
}
