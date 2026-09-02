using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// Callback entity — a queued caller's request to be phoned back instead of holding, created by
/// a tf_request_callback node and driven by the Worker's CallbackProcessingService. Covers the
/// full state machine from ARCHITECTURE.md §16: scheduled → attempted → completed | abandoned,
/// plus expired and cancelled, and the retry drop-back on a non-final no-answer.
/// </summary>
public class CallbackTests
{
    private static Callback New(TimeSpan? delay = null, int windowMinutes = 120, int maxAttempts = 3) =>
        Callback.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            delay ?? TimeSpan.Zero, windowMinutes, maxAttempts);

    [Fact]
    public void Create_SetsScheduledWindow_AndDefaults()
    {
        var before = DateTimeOffset.UtcNow;
        var cb = Callback.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " +1 555 000 1111 ",
            TimeSpan.FromMinutes(5), windowMinutes: 30, maxAttempts: 2);

        Assert.Equal(CallbackStatus.Scheduled, cb.Status);
        Assert.Equal("+1 555 000 1111", cb.CallbackNumber);
        Assert.Equal(0, cb.AttemptCount);
        Assert.Equal(2, cb.MaxAttempts);
        Assert.True(cb.RequestedAt >= before);
        Assert.NotNull(cb.ScheduledFor);
        Assert.NotNull(cb.ExpiresAt);
        Assert.Equal(TimeSpan.FromMinutes(30), cb.ExpiresAt!.Value - cb.ScheduledFor!.Value);
        Assert.True(cb.ScheduledFor!.Value >= before.AddMinutes(5).AddSeconds(-2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankNumber_Throws(string? number)
    {
        Assert.Throws<ArgumentException>(() =>
            Callback.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), number!, TimeSpan.Zero));
    }

    [Fact]
    public void Create_ClampsMaxAttemptsAndWindowToAtLeastOne()
    {
        var cb = New(windowMinutes: 0, maxAttempts: 0);
        Assert.Equal(1, cb.MaxAttempts);
        Assert.True(cb.ExpiresAt > cb.ScheduledFor);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  +15035550123 ", "+15035550123")]
    public void Create_NormalizesCallerIdOverride(string? input, string? expected)
    {
        var cb = Callback.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            TimeSpan.Zero, callerIdOverride: input);
        Assert.Equal(expected, cb.CallerIdOverride);
    }

    [Fact]
    public void Create_StoresDnisTheCallerDialed()
    {
        var cb = Callback.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            TimeSpan.Zero, dnis: " +15419196582 ");
        Assert.Equal("+15419196582", cb.Dnis);
        Assert.Null(cb.CallerIdOverride);   // blank override => the DID the caller dialed is used
    }

    [Fact]
    public void IsDue_TrueInsideOpenWindowWithAttemptsLeft()
    {
        var cb = New();
        Assert.True(cb.IsDue(DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public void IsDue_FalseBeforeWindowOpens()
    {
        var cb = New(delay: TimeSpan.FromMinutes(10));
        Assert.False(cb.IsDue(DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void IsDue_FalseAfterWindowCloses()
    {
        var cb = New(windowMinutes: 5);
        Assert.False(cb.IsDue(DateTimeOffset.UtcNow.AddMinutes(6)));
    }

    [Fact]
    public void MarkAttempted_MovesToAttempted_IncrementsCount_LinksOutboundRecord()
    {
        var cb = New();
        var outbound = Guid.NewGuid();

        cb.MarkAttempted(outbound);

        Assert.Equal(CallbackStatus.Attempted, cb.Status);
        Assert.Equal(1, cb.AttemptCount);
        Assert.Equal(outbound, cb.OutboundCallRecordId);
        Assert.NotNull(cb.LastAttemptAt);
    }

    [Fact]
    public void MarkAttempted_FromNonScheduled_Throws()
    {
        var cb = New();
        cb.MarkAttempted(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => cb.MarkAttempted(Guid.NewGuid()));
    }

    [Fact]
    public void MarkAttempted_NoRecordId_ThenLinkConnectedCallRecord()
    {
        var cb = New();
        cb.MarkAttempted();                       // outbound leg placed, connected record not yet created

        Assert.Equal(CallbackStatus.Attempted, cb.Status);
        Assert.Equal(1, cb.AttemptCount);
        Assert.Null(cb.OutboundCallRecordId);

        var connected = Guid.NewGuid();
        cb.LinkConnectedCallRecord(connected);
        Assert.Equal(connected, cb.OutboundCallRecordId);

        cb.MarkCompleted();
        Assert.Equal(CallbackStatus.Completed, cb.Status);
    }

    [Fact]
    public void MarkCompleted_FromAttempted_StampsCompletedAt()
    {
        var cb = New();
        cb.MarkAttempted(Guid.NewGuid());

        cb.MarkCompleted();

        Assert.Equal(CallbackStatus.Completed, cb.Status);
        Assert.NotNull(cb.CompletedAt);
        Assert.True(CallbackStatus.IsTerminal(cb.Status));
    }

    [Fact]
    public void MarkCompleted_FromScheduled_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => New().MarkCompleted());
    }

    [Fact]
    public void MarkNoAnswer_WithRetriesLeft_DropsBackToScheduled_ReturnsFalse()
    {
        var cb = New(maxAttempts: 3);
        cb.MarkAttempted(Guid.NewGuid());

        var abandoned = cb.MarkNoAnswer("no answer");

        Assert.False(abandoned);
        Assert.Equal(CallbackStatus.Scheduled, cb.Status);
        Assert.Equal(1, cb.AttemptCount);
        Assert.True(cb.IsDue(DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public void MarkNoAnswer_OnFinalAttempt_Abandons_ReturnsTrue()
    {
        var cb = New(maxAttempts: 2);
        cb.MarkAttempted(Guid.NewGuid());
        cb.MarkNoAnswer();                 // attempt 1 → back to scheduled
        cb.MarkAttempted(Guid.NewGuid());  // attempt 2

        var abandoned = cb.MarkNoAnswer("still no answer");

        Assert.True(abandoned);
        Assert.Equal(CallbackStatus.Abandoned, cb.Status);
        Assert.NotNull(cb.AbandonedAt);
        Assert.Equal("still no answer", cb.Detail);
    }

    [Fact]
    public void MarkExpired_FromScheduled_StampsExpiredAt()
    {
        var cb = New();
        cb.MarkExpired("window closed");

        Assert.Equal(CallbackStatus.Expired, cb.Status);
        Assert.NotNull(cb.ExpiredAt);
    }

    [Fact]
    public void MarkExpired_FromTerminal_Throws()
    {
        var cb = New();
        cb.MarkAttempted(Guid.NewGuid());
        cb.MarkCompleted();
        Assert.Throws<InvalidOperationException>(() => cb.MarkExpired());
    }

    [Fact]
    public void IsExpired_TrueForScheduledPastWindow()
    {
        var cb = New(windowMinutes: 5);
        Assert.True(cb.IsExpired(DateTimeOffset.UtcNow.AddMinutes(6)));
        Assert.False(cb.IsExpired(DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void Cancel_FromScheduled_RecordsReason()
    {
        var cb = New();
        cb.Cancel("caller phoned back in");

        Assert.Equal(CallbackStatus.Cancelled, cb.Status);
        Assert.Equal("caller phoned back in", cb.Detail);
        Assert.NotNull(cb.CancelledAt);
    }

    [Fact]
    public void Cancel_AfterAttempted_Throws()
    {
        var cb = New();
        cb.MarkAttempted(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => cb.Cancel("too late"));
    }

    [Theory]
    [InlineData(CallbackStatus.Completed, true)]
    [InlineData(CallbackStatus.Abandoned, true)]
    [InlineData(CallbackStatus.Expired, true)]
    [InlineData(CallbackStatus.Cancelled, true)]
    [InlineData(CallbackStatus.Scheduled, false)]
    [InlineData(CallbackStatus.Attempted, false)]
    public void IsTerminal_MatchesLifecycle(string status, bool expected)
    {
        Assert.Equal(expected, CallbackStatus.IsTerminal(status));
        Assert.True(CallbackStatus.IsValid(status));
    }
}
