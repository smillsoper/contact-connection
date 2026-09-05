using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Turns a designer "audio file id" into a FreeSWITCH-playable path/URI. Handles the shapes the
/// telephony designer emits: pass-through stream URIs (local_stream://, silence_stream://,
/// tone_stream://), built-in FreeSWITCH paths ("__builtin:/…"), platform phrase-library refs
/// ("__platform:{voice}/{phrase}" → the committed OGG under _platform/ on the shared sounds
/// volume), and tenant-uploaded file GUIDs (looked up in the tenant schema, resolved to a
/// container path under SoundsContainerPath).
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

        if (audioFileId.StartsWith("__platform:"))
            return ResolvePlatformPhraseArg(config, audioFileId["__platform:".Length..]);

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

    /// <summary>
    /// "__platform:{voice}/{phrase}" (the part after the prefix, passed here as
    /// <paramref name="voiceAndPhrase"/>) → the committed OGG at _platform/{voice}/{phrase}.ogg on
    /// the shared sounds volume. Platform-wide (not tenant-scoped), synthesized once by
    /// scripts/generate-platform-phrases.mjs, so no DB lookup and no tenant schema segment. Both
    /// path segments are restricted to [a-z0-9_] — anything else (incl. traversal) yields null.
    /// Public so PlayNodeHandler/WhisperNodeHandler's own copies of the resolve switch can share it.
    /// </summary>
    public static string? ResolvePlatformPhraseArg(IConfiguration config, string voiceAndPhrase)
    {
        var parts = voiceAndPhrase.Trim('/').Split('/');
        if (parts.Length != 2 || !parts.All(IsSafeSegment))
            return null;

        var containerBase = config["FreeSWITCH:SoundsContainerPath"]
            ?? "/usr/share/freeswitch/sounds/contactconnection";

        return $"{containerBase}/_platform/{parts[0]}/{parts[1]}.ogg";
    }

    private static bool IsSafeSegment(string s) =>
        s.Length > 0 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}
