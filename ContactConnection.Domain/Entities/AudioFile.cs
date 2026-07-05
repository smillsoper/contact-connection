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
}
