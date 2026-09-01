using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// The recording transcode/merge job state machine: pending → processing → complete | failed |
/// skipped, with linear backoff on retryable failures and a MaxAttempts ceiling. Drives the
/// RecordingMergeService worker. See DevLog "ffmpeg transcode/merge worker" / ARCHITECTURE.md §16.
/// </summary>
public class RecordingMergeJobTests
{
    private static RecordingMergeJob New() =>
        RecordingMergeJob.Create(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Create_StartsPending_AndImmediatelyClaimable()
    {
        var job = New();

        Assert.Equal(RecordingMergeJobStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Equal(5, job.MaxAttempts);
        Assert.True(job.IsClaimable(DateTimeOffset.UtcNow));
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void Claim_TransitionsToProcessing_IncrementsAttempts_StampsStartedAt()
    {
        var job = New();
        var now = DateTimeOffset.UtcNow;

        job.Claim(now);

        Assert.Equal(RecordingMergeJobStatus.Processing, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(now, job.StartedAt);
        Assert.False(job.IsClaimable(now));
    }

    [Fact]
    public void Claim_WhenNotYetDue_Throws()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);
        job.Fail("boom", TimeSpan.FromMinutes(5));   // back to pending, NextAttemptAt in the future

        Assert.False(job.IsClaimable(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => job.Claim(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Claim_WhenProcessing_Throws()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => job.Claim(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Succeed_SetsResultFields_AndCompletes()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);
        var screenId = Guid.NewGuid();

        job.Succeed("recordings/abc/merged.mp4", "mp4", 42_000, hadVideo: true,
            screenRecordingId: screenId, screenRecordingCount: 2, ffmpegCommand: "ffmpeg -i a.wav ...");

        Assert.Equal(RecordingMergeJobStatus.Complete, job.Status);
        Assert.Equal("recordings/abc/merged.mp4", job.OutputBlobKey);
        Assert.Equal("mp4", job.OutputFormat);
        Assert.Equal(42_000, job.OutputDurationMs);
        Assert.True(job.HadVideo);
        Assert.Equal(screenId, job.ScreenRecordingId);
        Assert.Equal(2, job.ScreenRecordingCount);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.LastError);
    }

    [Fact]
    public void Succeed_ZeroDuration_NormalisesToNull()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);

        job.Succeed("k", "m4a", 0, false, null, 0, "cmd");

        Assert.Null(job.OutputDurationMs);
    }

    [Fact]
    public void Fail_BelowMaxAttempts_ReturnsToPending_WithFutureBackoff()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);   // attempts = 1

        var before = DateTimeOffset.UtcNow;
        job.Fail("ffmpeg exit 1", TimeSpan.FromMinutes(5));

        Assert.Equal(RecordingMergeJobStatus.Pending, job.Status);
        Assert.Equal("ffmpeg exit 1", job.LastError);
        Assert.True(job.NextAttemptAt >= before.AddMinutes(5).AddSeconds(-2));
        Assert.False(job.IsClaimable(DateTimeOffset.UtcNow));
        Assert.True(job.IsClaimable(job.NextAttemptAt));
    }

    [Fact]
    public void Fail_AtMaxAttempts_BecomesFailed_AndStaysFailed()
    {
        var job = New();
        for (var i = 0; i < 5; i++)
        {
            job.Claim(job.NextAttemptAt);
            job.Fail($"attempt {i}", TimeSpan.Zero);
        }

        Assert.Equal(5, job.Attempts);
        Assert.Equal(RecordingMergeJobStatus.Failed, job.Status);
        Assert.False(job.IsClaimable(DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public void Fail_NonPositiveBackoff_FallsBackToOneMinute()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);

        var before = DateTimeOffset.UtcNow;
        job.Fail("x", TimeSpan.Zero);

        Assert.True(job.NextAttemptAt >= before.AddMinutes(1).AddSeconds(-2));
    }

    [Fact]
    public void Skip_IsTerminal()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);

        job.Skip("recording not retained");

        Assert.Equal(RecordingMergeJobStatus.Skipped, job.Status);
        Assert.Equal("recording not retained", job.LastError);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public void Requeue_ResetsToFreshPending()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);
        job.Fail("boom", TimeSpan.FromHours(1));

        job.Requeue();

        Assert.Equal(RecordingMergeJobStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.LastError);
        Assert.Null(job.StartedAt);
        Assert.True(job.IsClaimable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Fail_TruncatesVeryLongError()
    {
        var job = New();
        job.Claim(DateTimeOffset.UtcNow);

        job.Fail(new string('x', 5000), TimeSpan.FromMinutes(1));

        Assert.Equal(2000, job.LastError!.Length);
    }
}
