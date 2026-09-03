using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// ScheduledCallbackConnectionService lands a fired callback's terminal state from the ESL path:
/// MarkConnectedAsync (leg answered → completed + linked record) and MarkNoAnswerAsync
/// (leg failed → reschedule while retries remain, else abandon + a callback_abandon row).
/// </summary>
public class ScheduledCallbackConnectionServiceTests
{
    private const string Schema = "tenant_test_tenant";
    private static readonly Guid TenantId = Guid.Parse("dddddddd-0000-0000-0000-0000000000aa");

    private static string DbName => Guid.NewGuid().ToString();
    private static TenantDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(name).Options);

    private readonly Mock<ICallStateHistoryRecorder> _stateRecorder = new();

    private ScheduledCallbackConnectionService NewService(string dbName)
    {
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(() => Db(dbName));
        return new ScheduledCallbackConnectionService(
            factory.Object, _stateRecorder.Object, NullLogger<ScheduledCallbackConnectionService>.Instance);
    }

    private static ScheduledCallback Attempted(string dbName, int maxAttempts = 3, int attemptsMade = 1)
    {
        var cb = ScheduledCallback.Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), "+15551234567",
            DateTimeOffset.UtcNow.AddMinutes(-1), windowMinutes: 120, maxAttempts: maxAttempts);
        for (var i = 0; i < attemptsMade; i++)
        {
            cb.MarkAttempted();
            if (i < attemptsMade - 1) cb.MarkNoAnswer();  // back to scheduled between attempts
        }
        using var db = Db(dbName);
        db.ScheduledCallbacks.Add(cb);
        db.SaveChanges();
        return cb;
    }

    [Fact]
    public async Task MarkConnected_FromAttempted_Completes_AndLinksRecord()
    {
        var dbName = DbName;
        var cb = Attempted(dbName);
        var connectedRecord = Guid.NewGuid();

        var ok = await NewService(dbName).MarkConnectedAsync(Schema, TenantId, cb.Id, connectedRecord);

        Assert.True(ok);
        await using var check = Db(dbName);
        var stored = await check.ScheduledCallbacks.FindAsync(cb.Id);
        Assert.Equal(ScheduledCallbackStatus.Completed, stored!.Status);
        Assert.Equal(connectedRecord, stored.OutboundCallRecordId);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task MarkConnected_WhenNotAttempted_IsNoOp()
    {
        var dbName = DbName;
        var cb = ScheduledCallback.Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), "+15551234567", DateTimeOffset.UtcNow.AddMinutes(-1));
        using (var db = Db(dbName)) { db.ScheduledCallbacks.Add(cb); db.SaveChanges(); }  // still 'scheduled'

        var ok = await NewService(dbName).MarkConnectedAsync(Schema, TenantId, cb.Id, Guid.NewGuid());

        Assert.False(ok);
        await using var check = Db(dbName);
        Assert.Equal(ScheduledCallbackStatus.Scheduled, (await check.ScheduledCallbacks.FindAsync(cb.Id))!.Status);
    }

    [Fact]
    public async Task MarkConnected_UnknownId_ReturnsFalse()
    {
        var dbName = DbName;
        var ok = await NewService(dbName).MarkConnectedAsync(Schema, TenantId, Guid.NewGuid(), Guid.NewGuid());
        Assert.False(ok);
    }

    [Fact]
    public async Task MarkNoAnswer_WithRetriesLeft_Reschedules_NoAbandonRow()
    {
        var dbName = DbName;
        var cb = Attempted(dbName, maxAttempts: 3, attemptsMade: 1);

        var abandoned = await NewService(dbName).MarkNoAnswerAsync(Schema, TenantId, cb.Id, "NO_ANSWER");

        Assert.False(abandoned);
        await using var check = Db(dbName);
        Assert.Equal(ScheduledCallbackStatus.Scheduled, (await check.ScheduledCallbacks.FindAsync(cb.Id))!.Status);
        _stateRecorder.Verify(r => r.RecordAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkNoAnswer_OnFinalAttempt_Abandons_AndRecordsCallbackAbandon()
    {
        var dbName = DbName;
        var cb = Attempted(dbName, maxAttempts: 2, attemptsMade: 2);

        var abandoned = await NewService(dbName).MarkNoAnswerAsync(Schema, TenantId, cb.Id, "USER_BUSY");

        Assert.True(abandoned);
        await using var check = Db(dbName);
        Assert.Equal(ScheduledCallbackStatus.Abandoned, (await check.ScheduledCallbacks.FindAsync(cb.Id))!.Status);
        _stateRecorder.Verify(r => r.RecordAsync(
            TenantId, Schema, cb.CallRecordId, CallHistoryState.Abandoned, cb.CampaignId,
            null, It.Is<string>(s => s.Contains("Scheduled callback abandoned")),
            CallAbandonType.CallbackAbandon, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
