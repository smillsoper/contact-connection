using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class ScheduledCallbackNodeHandlerTests
{
    private const string Uuid = "call-uuid-cb";
    private static readonly Guid CallId     = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TenantId   = Guid.Parse("cccccccc-0000-0000-0000-0000000000aa");
    private static readonly Guid CampaignId = Guid.Parse("cccccccc-0000-0000-0000-0000000000bb");
    private static readonly Guid TargetFlow = Guid.Parse("cccccccc-0000-0000-0000-0000000000ff");

    // Far enough out that any timezone offset still leaves it comfortably in the future.
    private static readonly string FutureDate = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd");
    private const string FutureTime = "14:00";

    private readonly Mock<ICallStateHistoryRecorder> _stateRecorder = new();
    private readonly Mock<ITelephonyCallSessionStore> _sessionStore = new();

    private static string DbName => Guid.NewGuid().ToString();
    private static TenantDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(name).Options);

    private ScheduledCallbackNodeHandler NewHandler(string dbName, TelephonyCallSession? session = null)
    {
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(() => Db(dbName));
        _sessionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        return new ScheduledCallbackNodeHandler(
            factory.Object, _stateRecorder.Object, _sessionStore.Object,
            NullLogger<ScheduledCallbackNodeHandler>.Instance);
    }

    private static TelephonyFlowContext Ctx(string caller = "+15551110000")
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = Uuid, CallerNumber = caller, DestinationNumber = "+15552220000",
            TenantId = TenantId, CampaignId = CampaignId, CallRecordId = CallId,
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant",
            TenantTimezone = "America/Chicago", Esl = null,
        };
        ctx.Vars["_queued"] = "true";
        return ctx;
    }

    private static JsonObject Node(JsonObject? overrides = null)
    {
        var n = new JsonObject
        {
            ["type"]   = "tf_scheduled_callback",
            ["nodeId"] = "tf_scheduled_callback_1",
            ["scheduledDateValue"] = FutureDate,
            ["scheduledTimeValue"] = FutureTime,
            ["targetFlowId"] = TargetFlow.ToString(),
            ["windowMinutes"] = 60,
            ["maxAttempts"] = 2,
            ["transitions"] = new JsonObject
            {
                ["scheduled"]    = "node_thanks",
                ["invalid_time"] = "node_bad_time",
                ["failed"]       = "node_back_to_hold",
                ["default"]      = "node_thanks",
            },
        };
        if (overrides is not null)
            foreach (var kv in overrides) n[kv.Key] = kv.Value?.DeepClone();
        return n;
    }

    [Fact]
    public async Task ValidFutureTime_CreatesRow_Dequeues_FollowsScheduled()
    {
        var dbName = DbName;
        var ctx = Ctx();

        var result = await NewHandler(dbName).ExecuteAsync(Node(), ctx);

        Assert.Equal("scheduled", result.TransitionTaken);
        Assert.Equal("node_thanks", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_queued"));
        // The engine's post-node session-sync only copies ctx.Vars in — the handler must also
        // register _queued for deletion or the ivr_done resume path re-persists it (the caller
        // stays deliverable to an agent after booking a callback).
        Assert.Contains("_queued", ctx.VarsToRemove);
        Assert.True(ctx.Vars.ContainsKey("_scheduled_callback_id"));

        await using var check = Db(dbName);
        var cb = Assert.Single(check.ScheduledCallbacks);
        Assert.Equal(ScheduledCallbackStatus.Scheduled, cb.Status);
        Assert.Equal("+15551110000", cb.CallbackNumber);
        Assert.Equal(CampaignId, cb.CampaignId);
        Assert.Equal("+15552220000", cb.Dnis);
        Assert.Equal(TargetFlow, cb.TargetFlowId);
        Assert.Equal(2, cb.MaxAttempts);
        Assert.True(cb.ScheduledFor > DateTimeOffset.UtcNow);

        _stateRecorder.Verify(r => r.RecordAsync(
            TenantId, "tenant_test_tenant", CallId, CallHistoryState.PostAgent, CampaignId,
            null, It.Is<string>(s => s.Contains("Callback scheduled")),
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearsQueuedOnPersistedSession_StampsLeftForCallback()
    {
        var dbName = DbName;
        var session = new TelephonyCallSession
        {
            ChannelUuid = Uuid,
            Vars = new Dictionary<string, string> { ["_queued"] = "true", ["_in_queue_at"] = "x" },
        };

        await NewHandler(dbName, session).ExecuteAsync(Node(), Ctx());

        Assert.False(session.Vars.ContainsKey("_queued"));
        Assert.Equal("true", session.Vars["_left_for_callback"]);
        Assert.True(session.Vars.ContainsKey("_scheduled_callback_id"));
        _sessionStore.Verify(s => s.SaveAsync(session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithheldAni_NoCollectedVar_FollowsFailed_NoRow()
    {
        var dbName = DbName;
        var ctx = Ctx(caller: "anonymous");

        var result = await NewHandler(dbName).ExecuteAsync(Node(), ctx);

        Assert.Equal("failed", result.TransitionTaken);
        Assert.Equal("node_back_to_hold", result.NextNodeId);
        Assert.True(ctx.Vars.ContainsKey("_queued"));

        await using var check = Db(dbName);
        Assert.Empty(check.ScheduledCallbacks);
    }

    [Fact]
    public async Task PastDateTime_FollowsInvalidTime_NoRow()
    {
        var dbName = DbName;
        var node = Node(new JsonObject { ["scheduledDateValue"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") });

        var result = await NewHandler(dbName).ExecuteAsync(node, Ctx());

        Assert.Equal("invalid_time", result.TransitionTaken);
        Assert.Equal("node_bad_time", result.NextNodeId);
        await using var check = Db(dbName);
        Assert.Empty(check.ScheduledCallbacks);
    }

    [Fact]
    public async Task UnparseableDate_FollowsFailed_NoRow()
    {
        var dbName = DbName;
        var node = Node(new JsonObject { ["scheduledDateValue"] = "sometime next week" });

        var result = await NewHandler(dbName).ExecuteAsync(node, Ctx());

        Assert.Equal("failed", result.TransitionTaken);
        await using var check = Db(dbName);
        Assert.Empty(check.ScheduledCallbacks);
    }

    [Fact]
    public async Task OutsideAllowedDayOrHours_FollowsInvalidTime()
    {
        var dbName = DbName;
        // Allow only 08:00–12:00; the node's default time is 14:00.
        var node = Node(new JsonObject { ["allowedStartTime"] = "08:00", ["allowedEndTime"] = "12:00" });

        var result = await NewHandler(dbName).ExecuteAsync(node, Ctx());

        Assert.Equal("invalid_time", result.TransitionTaken);
        await using var check = Db(dbName);
        Assert.Empty(check.ScheduledCallbacks);
    }

    [Fact]
    public async Task DateAndTimeFromVariables_Resolved()
    {
        var dbName = DbName;
        var ctx = Ctx();
        ctx.Vars["cb_date"] = FutureDate;
        ctx.Vars["cb_time"] = "10:30";

        var node = Node(new JsonObject
        {
            ["scheduledDateValue"] = "{{flow.cb_date}}",
            ["scheduledTimeValue"] = "{{flow.cb_time}}",
        });

        var result = await NewHandler(dbName).ExecuteAsync(node, ctx);

        Assert.Equal("scheduled", result.TransitionTaken);
        await using var check = Db(dbName);
        Assert.Single(check.ScheduledCallbacks);
    }

    [Fact]
    public async Task CollectedNumberSource_ReadsSessionVar()
    {
        var dbName = DbName;
        var ctx = Ctx(caller: "anonymous");
        ctx.Vars["cb_num"] = "+15559998888";

        var node = Node(new JsonObject { ["numberSource"] = "collected", ["collectedVar"] = "cb_num" });
        var result = await NewHandler(dbName).ExecuteAsync(node, ctx);

        Assert.Equal("scheduled", result.TransitionTaken);
        await using var check = Db(dbName);
        Assert.Equal("+15559998888", (await check.ScheduledCallbacks.SingleAsync()).CallbackNumber);
    }

    [Fact]
    public async Task CallerIdOverride_LiteralAndVariable_FrozenToLiteral()
    {
        var dbName = DbName;
        var ctx = Ctx();
        ctx.Vars["client_did"] = "+15035559999";

        await NewHandler(dbName).ExecuteAsync(Node(new JsonObject { ["callerIdOverride"] = "+18005551212" }), ctx);
        await using (var check = Db(dbName))
            Assert.Equal("+18005551212", (await check.ScheduledCallbacks.SingleAsync()).CallerIdOverride);

        var dbName2 = DbName;
        await NewHandler(dbName2).ExecuteAsync(Node(new JsonObject { ["callerIdOverride"] = "{{flow.client_did}}" }), ctx);
        await using (var check2 = Db(dbName2))
            Assert.Equal("+15035559999", (await check2.ScheduledCallbacks.SingleAsync()).CallerIdOverride);
    }
}
