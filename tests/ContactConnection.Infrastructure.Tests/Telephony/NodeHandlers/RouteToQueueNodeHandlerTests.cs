using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>Covers RouteToQueueNodeHandler's MaxQueueSize enforcement — a new call arriving when
/// the campaign's queue is already at/over the limit must not enter the queue, must record a
/// QueueFull abandon, and must either follow a wired "on_timeout" transition or hang up rather
/// than leave the caller silently parked.</summary>
public class RouteToQueueNodeHandlerTests
{
    private static TenantDbContext NewDb(Campaign campaign)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TenantDbContext(options);
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return db;
    }

    private static RouteToQueueNodeHandler NewHandler(
        TenantDbContext db, Mock<ITelephonyCallSessionStore> sessionStore, Mock<ICallStateHistoryRecorder> callStateRecorder,
        out Mock<IAgentStateStore> stateStore)
    {
        var dbFactory = new Mock<ITenantDbContextFactory>();
        dbFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        stateStore = new Mock<IAgentStateStore>();
        var ranker = new EligibleAgentRanker(stateStore.Object);

        return new RouteToQueueNodeHandler(dbFactory.Object, callStateRecorder.Object, ranker, sessionStore.Object);
    }

    private static TelephonyFlowContext NewContext(Guid campaignId, IEslCommander? esl = null) => new()
    {
        ChannelUuid       = Guid.NewGuid().ToString(),
        CallerNumber      = "+15551234567",
        DestinationNumber = "+15559876543",
        TenantId          = Guid.NewGuid(),
        CampaignId        = campaignId,
        CallRecordId      = Guid.NewGuid(),
        TenantSubdomain   = "test-tenant",
        TenantSchemaName  = "tenant_test_tenant",
        TenantTimezone    = "America/Chicago",
        Esl               = esl,
    };

    private static List<TelephonyCallSession> QueuedSessions(Guid campaignId, int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new TelephonyCallSession
            {
                ChannelUuid = Guid.NewGuid().ToString(),
                CampaignId  = campaignId,
                Vars        = new Dictionary<string, string> { ["_queued"] = "true" },
            })
            .ToList();

    [Fact]
    public async Task QueueAtCapacity_NoTimeoutTransitionWired_HangsUpAndRecordsQueueFullAbandon()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", "test");
        campaign.Update(
            name: "Test", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: 5, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 2, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: false, queueAccelerationIntervalSeconds: 60, queueAccelerationPriorityBoost: 1,
            ringStrategy: CampaignRingStrategy.RingAll, ringTopN: 3);

        await using var db = NewDb(campaign);

        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueuedSessions(campaign.Id, count: 2)); // already at MaxQueueSize

        var callStateRecorder = new Mock<ICallStateHistoryRecorder>();
        var eslMock = new Mock<IEslCommander>();

        var handler = NewHandler(db, sessionStore, callStateRecorder, out _);
        var node = new JsonObject(); // no "transitions" at all — no on_timeout wired
        var ctx = NewContext(campaign.Id, eslMock.Object);

        var result = await handler.ExecuteAsync(node, ctx);

        Assert.Equal("queue_full", result.TransitionTaken);
        Assert.Null(result.NextNodeId);
        Assert.DoesNotContain("_queued", ctx.Vars.Keys);
        eslMock.Verify(e => e.HangupChannelAsync(ctx.ChannelUuid, It.IsAny<CancellationToken>()), Times.Once);
        callStateRecorder.Verify(r => r.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.Abandoned, campaign.Id, null, It.IsAny<string?>(),
            CallAbandonType.QueueFull, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueueAtCapacity_OnTimeoutTransitionWired_TakesThatTransition_NoHangup()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", "test");
        campaign.Update(
            name: "Test", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: 5, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 1, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: false, queueAccelerationIntervalSeconds: 60, queueAccelerationPriorityBoost: 1,
            ringStrategy: CampaignRingStrategy.RingAll, ringTopN: 3);

        await using var db = NewDb(campaign);

        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueuedSessions(campaign.Id, count: 1)); // already at MaxQueueSize (1)

        var callStateRecorder = new Mock<ICallStateHistoryRecorder>();
        var eslMock = new Mock<IEslCommander>();

        var handler = NewHandler(db, sessionStore, callStateRecorder, out _);
        var node = new JsonObject
        {
            ["transitions"] = new JsonObject { ["on_timeout"] = "node_voicemail" },
        };
        var ctx = NewContext(campaign.Id, eslMock.Object);

        var result = await handler.ExecuteAsync(node, ctx);

        Assert.Equal("queue_full", result.TransitionTaken);
        Assert.Equal("node_voicemail", result.NextNodeId);
        eslMock.Verify(e => e.HangupChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnteringQueue_CancelsCallerPendingCallbackForThisCampaign()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", "test");
        campaign.Update(
            name: "Test", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: 5, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 5, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: false, queueAccelerationIntervalSeconds: 60, queueAccelerationPriorityBoost: 1,
            ringStrategy: CampaignRingStrategy.RingAll, ringTopN: 3);

        var dbName = Guid.NewGuid().ToString();
        TenantDbContext Open() => new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(dbName).Options);

        // caller "+15551234567" (the NewContext ANI) had booked a callback on this campaign,
        // plus an unrelated one on another campaign and one already terminal — only the first cancels.
        var mine     = Callback.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.Id, "+15551234567", TimeSpan.Zero);
        var other    = Callback.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "+15551234567", TimeSpan.Zero);
        var done     = Callback.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.Id, "+15551234567", TimeSpan.Zero);
        done.MarkAttempted(); done.MarkCompleted();
        using (var seed = Open())
        {
            seed.Campaigns.Add(campaign);
            seed.Callbacks.AddRange(mine, other, done);
            seed.SaveChanges();
        }

        var dbFactory = new Mock<ITenantDbContextFactory>();
        dbFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(Open);

        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelephonyCallSession>());
        var ranker = new EligibleAgentRanker(new Mock<IAgentStateStore>().Object);
        var handler = new RouteToQueueNodeHandler(
            dbFactory.Object, new Mock<ICallStateHistoryRecorder>().Object, ranker, sessionStore.Object);

        var result = await handler.ExecuteAsync(new JsonObject(), NewContext(campaign.Id));

        Assert.Equal("queued", result.TransitionTaken);
        await using var check = Open();
        Assert.Equal(CallbackStatus.Cancelled, (await check.Callbacks.FindAsync(mine.Id))!.Status);
        Assert.Equal(CallbackStatus.Scheduled, (await check.Callbacks.FindAsync(other.Id))!.Status);
        Assert.Equal(CallbackStatus.Completed, (await check.Callbacks.FindAsync(done.Id))!.Status);
    }

    [Fact]
    public async Task QueueBelowCapacity_EntersQueueNormally()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", "test");
        campaign.Update(
            name: "Test", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: 5, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 5, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: false, queueAccelerationIntervalSeconds: 60, queueAccelerationPriorityBoost: 1,
            ringStrategy: CampaignRingStrategy.RingAll, ringTopN: 3);

        await using var db = NewDb(campaign);

        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(QueuedSessions(campaign.Id, count: 1)); // well under MaxQueueSize (5)

        var callStateRecorder = new Mock<ICallStateHistoryRecorder>();

        var handler = NewHandler(db, sessionStore, callStateRecorder, out _);
        var node = new JsonObject();
        var ctx = NewContext(campaign.Id);

        var result = await handler.ExecuteAsync(node, ctx);

        Assert.Equal("queued", result.TransitionTaken);
        Assert.Equal("true", ctx.Vars["_queued"]);
        callStateRecorder.Verify(r => r.RecordAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            CallHistoryState.InQueue, It.IsAny<Guid>(), null, null,
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
