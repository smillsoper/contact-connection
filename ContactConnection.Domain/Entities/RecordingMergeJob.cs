namespace ContactConnection.Domain.Entities;

/// <summary>
/// Tracks the post-call transcode / A/V-merge of one call's recording: the stereo call-audio
/// WAV written by <c>uuid_record</c> plus any agent-side screen captures, combined by the
/// <c>RecordingMergeService</c> worker into a single playable artifact in blob storage.
///
/// One row per call record (unique). The job is the orchestration state — attempts, backoff,
/// last error, the ffmpeg command line, the output blob key — kept off the call record's hot
/// columns. The merged artifact itself is the call record's truth: on success the worker sets
/// <c>CallRecord.RecordingUrl</c>.
///
/// Lifecycle: <c>pending</c> → <c>processing</c> → <c>complete</c> | <c>failed</c> | <c>skipped</c>.
/// A failed attempt below <see cref="MaxAttempts"/> returns to <c>pending</c> with a future
/// <see cref="NextAttemptAt"/> (linear backoff). See ARCHITECTURE.md §13 / §14 / §16.
/// </summary>
public class RecordingMergeJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }

    public string Status { get; private set; } = RecordingMergeJobStatus.Pending;

    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; } = 5;

    /// <summary>Earliest this job may be claimed again. Set to now on create, pushed out on each failure.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    // ── Result (set on Succeed) ──────────────────────────────────────────────
    /// <summary>Blob key of the merged output, e.g. <c>recordings/{callRecordId}/merged.mp4</c>.</summary>
    public string? OutputBlobKey { get; private set; }
    /// <summary><c>mp4</c> (audio + screen video) or <c>m4a</c> (audio only — no usable screen capture).</summary>
    public string? OutputFormat { get; private set; }
    public long? OutputDurationMs { get; private set; }
    public bool HadVideo { get; private set; }
    /// <summary>The screen recording chosen as the video track, when one was.</summary>
    public Guid? ScreenRecordingId { get; private set; }
    /// <summary>How many completed screen captures existed for the call (warm transfer → more than one).</summary>
    public int ScreenRecordingCount { get; private set; }
    /// <summary>The exact ffmpeg argument vector of the successful run — kept for debugging / reproduction.</summary>
    public string? FfmpegCommand { get; private set; }

    // ── Audit ────────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private RecordingMergeJob() { }

    public static RecordingMergeJob Create(Guid tenantId, Guid callRecordId)
    {
        var now = DateTimeOffset.UtcNow;
        return new RecordingMergeJob
        {
            Id            = Guid.NewGuid(),
            TenantId      = tenantId,
            CallRecordId  = callRecordId,
            Status        = RecordingMergeJobStatus.Pending,
            Attempts      = 0,
            NextAttemptAt = now,
            CreatedAt     = now,
            UpdatedAt     = now,
        };
    }

    /// <summary>Returns true when the job is claimable right now (pending and its backoff has elapsed).</summary>
    public bool IsClaimable(DateTimeOffset now) =>
        Status == RecordingMergeJobStatus.Pending && now >= NextAttemptAt;

    /// <summary>
    /// Transitions <c>pending</c> → <c>processing</c>, increments <see cref="Attempts"/>, stamps
    /// <see cref="StartedAt"/>. Throws if the job is not claimable — callers gate on
    /// <see cref="IsClaimable"/> (and the repository claims under a row lock).
    /// </summary>
    public void Claim(DateTimeOffset now)
    {
        if (!IsClaimable(now))
            throw new InvalidOperationException(
                $"RecordingMergeJob {Id} is not claimable (status={Status}, nextAttemptAt={NextAttemptAt:o}).");

        Status    = RecordingMergeJobStatus.Processing;
        Attempts += 1;
        StartedAt = now;
        UpdatedAt = now;
    }

    public void Succeed(
        string outputBlobKey,
        string outputFormat,
        long? outputDurationMs,
        bool hadVideo,
        Guid? screenRecordingId,
        int screenRecordingCount,
        string ffmpegCommand)
    {
        var now = DateTimeOffset.UtcNow;
        Status               = RecordingMergeJobStatus.Complete;
        OutputBlobKey        = outputBlobKey;
        OutputFormat         = outputFormat;
        OutputDurationMs     = outputDurationMs is > 0 ? outputDurationMs : null;
        HadVideo             = hadVideo;
        ScreenRecordingId    = screenRecordingId;
        ScreenRecordingCount = screenRecordingCount;
        FfmpegCommand        = ffmpegCommand;
        LastError            = null;
        CompletedAt          = now;
        UpdatedAt            = now;
    }

    /// <summary>
    /// Records a failed attempt. Below <see cref="MaxAttempts"/> the job returns to <c>pending</c>
    /// with <see cref="NextAttemptAt"/> pushed out by <paramref name="backoff"/>; at the limit it
    /// becomes <c>failed</c> and is not retried automatically.
    /// </summary>
    public void Fail(string error, TimeSpan backoff)
    {
        var now = DateTimeOffset.UtcNow;
        LastError = Truncate(error, 2000);
        UpdatedAt = now;

        if (Attempts >= MaxAttempts)
        {
            Status = RecordingMergeJobStatus.Failed;
        }
        else
        {
            Status        = RecordingMergeJobStatus.Pending;
            NextAttemptAt = now + (backoff > TimeSpan.Zero ? backoff : TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>Terminal, non-error outcome — nothing to merge (audio file gone past its retention window, recording not retained, etc.).</summary>
    public void Skip(string reason)
    {
        var now   = DateTimeOffset.UtcNow;
        Status    = RecordingMergeJobStatus.Skipped;
        LastError = Truncate(reason, 2000);
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Force a failed/skipped job back into the queue (operator-initiated re-merge).</summary>
    public void Requeue()
    {
        var now       = DateTimeOffset.UtcNow;
        Status        = RecordingMergeJobStatus.Pending;
        Attempts      = 0;
        NextAttemptAt = now;
        LastError     = null;
        StartedAt     = null;
        CompletedAt   = null;
        UpdatedAt     = now;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}

public static class RecordingMergeJobStatus
{
    public const string Pending    = "pending";
    public const string Processing = "processing";
    public const string Complete   = "complete";
    public const string Failed     = "failed";
    public const string Skipped    = "skipped";

    public static bool IsValid(string value) =>
        value is Pending or Processing or Complete or Failed or Skipped;
}
