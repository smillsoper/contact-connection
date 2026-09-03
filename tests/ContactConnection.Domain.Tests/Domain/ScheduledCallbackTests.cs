using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// ScheduledCallback entity — a callback booked for a specific future time, created by a
/// tf_scheduled_callback / scheduled_callback node and driven by the Worker's
/// ScheduledCallbackProcessingService. Covers the state machine from ARCHITECTURE.md §16:
/// scheduled → attempted → completed | abandoned, plus expired and cancelled, and the retry
/// drop-back on a non-final no-answer.
/// </summary>
public class ScheduledCallbackTests
{
    private static ScheduledCallback New(int minutesFromNow = 1, int windowMinutes = 120, int maxAttempts = 3) =>
        ScheduledCallback.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            DateTimeOffset.UtcNow.AddMinutes(minutesFromNow), windowMinutes, maxAttempts);

    [Fact]
    public void Create_SetsScheduledWindow_AndDefaults()
    {
        var when = DateTimeOffset.UtcNow.AddHours(3);
        var cb = ScheduledCallback.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " +1 555 000 1111 ",
            when, windowMinutes: 30, maxAttempts: 2,
            targetFlowId: Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.Equal(ScheduledCallbackStatus.Scheduled, cb.Status);
        Assert.Equal("+1 555 000 1111", cb.CallbackNumber);
        Assert.Equal(0, cb.AttemptCount);
        Assert.Equal(2, cb.MaxAttempts);
        Assert.Equal(when, cb.ScheduledFor);
        Assert.Equal(TimeSpan.FromMinutes(30), cb.ExpiresAt - cb.ScheduledFor);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), cb.TargetFlowId);
        Assert.Null(cb.TargetCampaignId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankNumber_Throws(string? number)
    {
        Assert.Throws<ArgumentException>(() =>
            ScheduledCallback.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), number!, DateTimeOffset.UtcNow.AddHours(1)));
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
        var cb = ScheduledCallback.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            DateTimeOffset.UtcNow.AddHours(1), callerIdOverride: input);
        Assert.Equal(expected, cb.CallerIdOverride);
    }

    [Fact]
    public void Create_StoresDnisTheCallerDialed()
    {
        var cb = ScheduledCallback.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            DateTimeOffset.UtcNow.AddHours(1), dnis: " +15419196582 ");
        Assert.Equal("+15419196582", cb.Dnis);
        Assert.Null(cb.CallerIdOverride);
    }

    [Fact]
    public void IsDue_TrueOnceBookedTimeHasArrived_WithAttemptsLeft()
    {
        var cb = New(minutesFromNow: 1);
        Assert.False(cb.IsDue(DateTimeOffset.UtcNow));                    // before the booked time
        Assert.True(cb.IsDue(DateTimeOffset.UtcNow.AddMinutes(2)));       // after it
    }

    [Fact]
    public void IsDue_FalseAfterAttemptWindowCloses()
    {
        var cb = New(minutesFromNow: 1, windowMinutes: 5);
        Assert.False(cb.IsDue(DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    [Fact]
    public void MarkAttempted_MovesToAttempted_IncrementsCount_LinksOutboundRecord()
    {
        var cb = New();
        var outbound = Guid.NewGuid();

        cb.MarkAttempted(outbound);

        Assert.Equal(ScheduledCallbackStatus.Attempted, cb.Status);
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
        cb.MarkAttempted();

        Assert.Equal(ScheduledCallbackStatus.Attempted, cb.Status);
        Assert.Null(cb.OutboundCallRecordId);

        var connected = Guid.NewGuid();
        cb.LinkConnectedCallRecord(connected);
        Assert.Equal(connected, cb.OutboundCallRecordId);

        cb.MarkCompleted();
        Assert.Equal(ScheduledCallbackStatus.Completed, cb.Status);
    }

    [Fact]
    public void MarkCompleted_FromAttempted_StampsCompletedAt()
    {
        var cb = New();
        cb.MarkAttempted(Guid.NewGuid());
        cb.MarkCompleted();

        Assert.Equal(ScheduledCallbackStatus.Completed, cb.Status);
        Assert.NotNull(cb.CompletedAt);
        Assert.True(ScheduledCallbackStatus.IsTerminal(cb.Status));
    }

    [Fact]
    public void MarkCompleted_FromScheduled_Throws() =>
        Assert.Throws<InvalidOperationException>(() => New().MarkCompleted());

    [Fact]
    public void MarkNoAnswer_WithRetriesLeft_DropsBackToScheduled_ReturnsFalse()
    {
        var cb = New(maxAttempts: 3);
        cb.MarkAttempted(Guid.NewGuid());

        var abandoned = cb.MarkNoAnswer("no answer");

        Assert.False(abandoned);
        Assert.Equal(ScheduledCallbackStatus.Scheduled, cb.Status);
        Assert.Equal(1, cb.AttemptCount);
        Assert.True(cb.IsDue(DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void MarkNoAnswer_OnFinalAttempt_Abandons_ReturnsTrue()
    {
        var cb = New(maxAttempts: 2);
        cb.MarkAttempted(Guid.NewGuid());
        cb.MarkNoAnswer();
        cb.MarkAttempted(Guid.NewGuid());

        var abandoned = cb.MarkNoAnswer("still no answer");

        Assert.True(abandoned);
        Assert.Equal(ScheduledCallbackStatus.Abandoned, cb.Status);
        Assert.NotNull(cb.AbandonedAt);
        Assert.Equal("still no answer", cb.Detail);
    }

    [Fact]
    public void MarkExpired_FromScheduled_StampsExpiredAt()
    {
        var cb = New();
        cb.MarkExpired("window closed");

        Assert.Equal(ScheduledCallbackStatus.Expired, cb.Status);
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
        var cb = New(minutesFromNow: 1, windowMinutes: 5);
        Assert.True(cb.IsExpired(DateTimeOffset.UtcNow.AddMinutes(10)));
        Assert.False(cb.IsExpired(DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void Cancel_FromScheduled_RecordsReason()
    {
        var cb = New();
        cb.Cancel("caller reached an agent another way");

        Assert.Equal(ScheduledCallbackStatus.Cancelled, cb.Status);
        Assert.Equal("caller reached an agent another way", cb.Detail);
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
    [InlineData(ScheduledCallbackStatus.Completed, true)]
    [InlineData(ScheduledCallbackStatus.Abandoned, true)]
    [InlineData(ScheduledCallbackStatus.Expired, true)]
    [InlineData(ScheduledCallbackStatus.Cancelled, true)]
    [InlineData(ScheduledCallbackStatus.Scheduled, false)]
    [InlineData(ScheduledCallbackStatus.Attempted, false)]
    public void IsTerminal_MatchesLifecycle(string status, bool expected)
    {
        Assert.Equal(expected, ScheduledCallbackStatus.IsTerminal(status));
        Assert.True(ScheduledCallbackStatus.IsValid(status));
    }
}
