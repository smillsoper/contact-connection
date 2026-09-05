namespace ContactConnection.Domain.Entities;

public class AudioFile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public string OriginalFileName { get; private set; } = "";
    public string StoredFileName { get; private set; } = "";  // {Id}.{ext}
    public string ContentType { get; private set; } = "";
    public long FileSizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Present only for a saved TTS clip (null for an uploaded/recorded file) — the text/provider/
    // voice that produced it, kept so the clip can be shown and re-synthesized in place later
    // rather than only ever being a nameless finished audio file.
    public string? TtsSourceText { get; private set; }
    public string? TtsProviderKey { get; private set; }
    public string? TtsVoiceId { get; private set; }

    public bool IsTtsGenerated => TtsProviderKey is not null;

    private AudioFile() { }

    public static AudioFile Create(
        Guid tenantId,
        string name,
        string originalFileName,
        string storedFileName,
        string contentType,
        long fileSizeBytes) => new()
    {
        Id               = Guid.NewGuid(),
        TenantId         = tenantId,
        Name             = name,
        OriginalFileName = originalFileName,
        StoredFileName   = storedFileName,
        ContentType      = contentType,
        FileSizeBytes    = fileSizeBytes,
        CreatedAt        = DateTimeOffset.UtcNow,
    };

    public static AudioFile CreateFromTts(
        Guid tenantId,
        string name,
        string storedFileName,
        string contentType,
        long fileSizeBytes,
        string sourceText,
        string providerKey,
        string voiceId) => new()
    {
        Id               = Guid.NewGuid(),
        TenantId         = tenantId,
        Name             = name,
        OriginalFileName = storedFileName,
        StoredFileName   = storedFileName,
        ContentType      = contentType,
        FileSizeBytes    = fileSizeBytes,
        CreatedAt        = DateTimeOffset.UtcNow,
        TtsSourceText    = sourceText,
        TtsProviderKey   = providerKey,
        TtsVoiceId       = voiceId,
    };

    /// <summary>
    /// Re-synthesizes an existing TTS clip in place — same Id (so every node still referencing it
    /// by <c>audioFileId</c> keeps working), new text/voice/audio. Only valid on a clip that was
    /// itself created via <see cref="CreateFromTts"/>; the caller is expected to have already
    /// overwritten the stored audio file at the unchanged <see cref="StoredFileName"/> path.
    /// </summary>
    public void RegenerateFromTts(
        string contentType, long fileSizeBytes, string sourceText, string providerKey, string voiceId, string? newName = null)
    {
        if (!IsTtsGenerated)
            throw new InvalidOperationException($"AudioFile {Id} was not created from TTS — cannot regenerate it.");

        ContentType    = contentType;
        FileSizeBytes  = fileSizeBytes;
        TtsSourceText  = sourceText;
        TtsProviderKey = providerKey;
        TtsVoiceId     = voiceId;
        if (!string.IsNullOrWhiteSpace(newName))
            Name = newName;
    }
}
