using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Turns a designer "audio file id" into a FreeSWITCH-playable path/URI. Handles the three
/// shapes the telephony designer emits: pass-through stream URIs (local_stream://, silence_stream://,
/// tone_stream://), built-in FreeSWITCH paths ("__builtin:/…"), and tenant-uploaded file GUIDs
/// (looked up in the tenant schema, resolved to a container path under SoundsContainerPath).
///
/// PlayNodeHandler and WhisperNodeHandler each carry their own private copy of this logic; new
/// call sites (IvrMenuNodeHandler) use this shared version.
/// </summary>
public static class TelephonyAudioResolver
{
    public static async Task<string?> ResolveFileArgAsync(
        ITenantDbContextFactory factory,
        IConfiguration config,
        string? audioFileId,
        string tenantSchemaName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(audioFileId))
            return null;

        if (audioFileId.StartsWith("local_stream://") ||
            audioFileId.StartsWith("silence_stream://") ||
            audioFileId.StartsWith("tone_stream://"))
            return audioFileId;

        if (audioFileId.StartsWith("__builtin:"))
            return audioFileId["__builtin:".Length..];

        if (!Guid.TryParse(audioFileId, out var fileId))
            return null;

        await using var db = factory.Create(tenantSchemaName);
        var audioFile = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (audioFile is null)
            return null;

        var containerBase = config["FreeSWITCH:SoundsContainerPath"]
            ?? "/usr/share/freeswitch/sounds/contactconnection";

        return $"{containerBase}/{tenantSchemaName}/{audioFile.StoredFileName}";
    }
}
