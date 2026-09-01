using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// One agent-side screen capture for a call (the browser extension records the ContactConnection
/// tab + declared client-app domains). Chunks stream in over HTTP and land in blob storage; this
/// row tracks progress, the clock relationship to the server, and the sync cue points the A/V
/// merge / synchronised player aligns against. One call can have several (warm transfer → a
/// segment per answering agent), stitched by their own <see cref="ClientClockOffsetMs"/>.
/// See ARCHITECTURE.md §13 / §14.
/// </summary>
public class ScreenRecording
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public Guid? InteractionId { get; private set; }
    public Guid AgentId { get; private set; }

    public string Status { get; private set; } = ScreenRecordingStatus.Recording;

    /// <summary>Container the extension is producing — <c>webm</c> or <c>mp4</c>.</summary>
    public string Container { get; private set; } = "webm";
    /// <summary>Free-text codec label from MediaRecorder, e.g. "vp9,opus" or "h264,opus".</summary>
    public string Codec { get; private set; } = string.Empty;

    /// <summary>Server clock at the moment the start request was received — the master time base.</summary>
    public DateTimeOffset StartedAtServer { get; private set; }
    /// <summary>The agent machine's own clock at capture start, as reported. Diagnostics only.</summary>
    public DateTimeOffset StartedAtClient { get; private set; }
    /// <summary>server − client, in milliseconds. Added to client-relative offsets to place them on the server timeline.</summary>
    public long ClientClockOffsetMs { get; private set; }

    /// <summary>Blob key prefix for this recording's chunks (and, later, its merged output).</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>Distinct chunk indices received so far, ascending. Lets the client resume an interrupted upload.</summary>
    public List<int> ReceivedChunkIndices { get; private set; } = [];
    public int ChunkCount => ReceivedChunkIndices.Count;
    public long TotalBytes { get; private set; }

    public long? DurationMs { get; private set; }
    public string? Sha256 { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Sync anchors mirrored from live call events (bridge / hold / mask / …), millis from capture start.</summary>
    public List<ScreenRecordingCuePoint> CuePoints { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private ScreenRecording() { }

    public static ScreenRecording Create(
        Guid tenantId,
        Guid callRecordId,
        Guid? interactionId,
        Guid agentId,
        string container,
        string codec,
        DateTimeOffset startedAtClient,
        DateTimeOffset serverNow)
    {
        var id = Guid.NewGuid();
        return new ScreenRecording
        {
            Id                  = id,
            TenantId            = tenantId,
            CallRecordId        = callRecordId,
            InteractionId       = interactionId,
            AgentId             = agentId,
            Container           = string.IsNullOrWhiteSpace(container) ? "webm" : container.Trim().ToLowerInvariant(),
            Codec               = codec?.Trim() ?? string.Empty,
            Status              = ScreenRecordingStatus.Recording,
            StartedAtServer     = serverNow,
            StartedAtClient     = startedAtClient,
            ClientClockOffsetMs = (long)Math.Round((serverNow - startedAtClient).TotalMilliseconds),
            StorageKey          = $"screen/{callRecordId}/{id}",
            CreatedAt           = serverNow,
            UpdatedAt           = serverNow,
        };
    }

    /// <summary>Records that chunk <paramref name="index"/> (<paramref name="byteLength"/> bytes) has landed. Idempotent.</summary>
    public void RegisterChunk(int index, long byteLength)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (Status is ScreenRecordingStatus.Complete or ScreenRecordingStatus.Aborted)
            throw new InvalidOperationException($"Cannot add chunks to a {Status} screen recording.");

        if (!ReceivedChunkIndices.Contains(index))
        {
            ReceivedChunkIndices.Add(index);
            ReceivedChunkIndices.Sort();
            TotalBytes += Math.Max(0, byteLength);   // re-PUT of an existing index is a replace; bytes not re-counted
        }

        if (Status == ScreenRecordingStatus.Recording)
            Status = ScreenRecordingStatus.Uploading;

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddCuePoint(long atMs, string kind, string? detail = null)
    {
        CuePoints.Add(new ScreenRecordingCuePoint
        {
            AtMs   = Math.Max(0, atMs),
            Kind   = ScreenRecordingCuePointKind.IsValid(kind) ? kind : ScreenRecordingCuePointKind.Custom,
            Detail = detail,
        });
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the upload finished. Throws if the received chunks aren't the contiguous run 0..N-1.</summary>
    public void MarkComplete(long durationMs, string? sha256)
    {
        if (Status is ScreenRecordingStatus.Aborted or ScreenRecordingStatus.Failed)
            throw new InvalidOperationException($"Cannot complete a {Status} screen recording.");

        for (var i = 0; i < ReceivedChunkIndices.Count; i++)
            if (ReceivedChunkIndices[i] != i)
                throw new InvalidOperationException(
                    $"Screen recording has a gap in its chunk sequence (missing index {i}); cannot complete.");

        DurationMs  = durationMs > 0 ? durationMs : null;
        Sha256      = sha256;
        Status      = ScreenRecordingStatus.Complete;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt   = CompletedAt.Value;
    }

    public void MarkFailed(string reason)
    {
        Status        = ScreenRecordingStatus.Failed;
        FailureReason = reason;
        UpdatedAt     = DateTimeOffset.UtcNow;
    }

    public void Abort()
    {
        Status    = ScreenRecordingStatus.Aborted;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public static class ScreenRecordingStatus
{
    public const string Recording = "recording";   // start received, no chunks yet
    public const string Uploading  = "uploading";   // chunks arriving
    public const string Complete   = "complete";
    public const string Failed     = "failed";
    public const string Aborted    = "aborted";

    public static bool IsValid(string value) =>
        value is Recording or Uploading or Complete or Failed or Aborted;
}

public static class ScreenRecordingCuePointKind
{
    public const string Bridge     = "bridge";
    public const string Hold       = "hold";
    public const string Unhold     = "unhold";
    public const string Mask       = "mask";
    public const string Unmask     = "unmask";
    public const string Disconnect = "disconnect";
    public const string Custom     = "custom";

    public static bool IsValid(string value) =>
        value is Bridge or Hold or Unhold or Mask or Unmask or Disconnect or Custom;
}
