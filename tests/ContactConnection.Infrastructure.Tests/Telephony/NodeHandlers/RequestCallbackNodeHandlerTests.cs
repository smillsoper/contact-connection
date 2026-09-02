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

public class RequestCallbackNodeHandlerTests
{
    private const string Uuid = "call-uuid-cb";
    private static readonly Guid CallId   = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("cccccccc-0000-0000-0000-0000000000aa");
    private static readonly Guid CampaignId = Guid.Parse("cccccccc-0000-0000-0000-0000000000bb");

    private readonly Mock<ICallStateHistoryRecorder> _stateRecorder = new();
    private readonly Mock<ITelephonyCallSessionStore> _sessionStore = new();

    private static string DbName => Guid.NewGuid().ToString();

    private static TenantDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(name).Options);

    private RequestCallbackNodeHandler NewHandler(string dbName, TelephonyCallSession? session = null)
    {
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(() => Db(dbName));
        _sessionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        return new RequestCallbackNodeHandler(
            factory.Object, _stateRecorder.Object, _sessionStore.Object, NullLogger<RequestCallbackNodeHandler>.Instance);
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
            ["type"] = "tf_request_callback",
            ["nodeId"] = "tf_request_callback_1",
            ["windowMinutes"] = 60,
            ["maxAttempts"] = 2,
            ["transitions"] = new JsonObject
            {
                ["requested"] = "node_thanks",
                ["failed"] = "node_back_to_hold",
                ["default"] = "node_thanks",
            },
        };
        if (overrides is not null)
            foreach (var kv in overrides) n[kv.Key] = kv.Value?.DeepClone();
        return n;
    }

    [Fact]
    public async Task Ani_CreatesScheduledCallback_Dequeues_FollowsRequested()
    {
        var dbName = DbName;
        var ctx = Ctx();

        var result = await NewHandler(dbName).ExecuteAsync(Node(), ctx);

        Assert.Equal("requested", result.TransitionTaken);
        Assert.Equal("node_thanks", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_queued"));
        Assert.True(ctx.Vars.ContainsKey("_callback_id"));

        await using var check = Db(dbName);
        var cb = Assert.Single(check.Callbacks);
        Assert.Equal(CallbackStatus.Scheduled, cb.Status);
        Assert.Equal("+15551110000", cb.CallbackNumber);
        Assert.Equal(CallId, cb.CallRecordId);
        Assert.Equal(CampaignId, cb.CampaignId);
        Assert.Equal(2, cb.MaxAttempts);
        Assert.Equal("+15552220000", cb.Dnis);   // ctx.DestinationNumber — the DID the caller dialed
        Assert.Equal(cb.Id.ToString(), ctx.Vars["_callback_id"]);

        _stateRecorder.Verify(r => r.RecordAsync(
            TenantId, "tenant_test_tenant", CallId, CallHistoryState.PostAgent, CampaignId,
            null, It.Is<string>(s => s.Contains("Callback requested")),
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ani_ClearsQueuedOnThePersistedSession_AndStampsLeftForCallback()
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
        Assert.True(session.Vars.ContainsKey("_callback_id"));
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
        Assert.True(ctx.Vars.ContainsKey("_queued")); // still queued — nothing changed

        await using var check = Db(dbName);
        Assert.Empty(check.Callbacks);
    }

    [Fact]
    public async Task CollectedNumberSource_ReadsSessionVar()
    {
        var dbName = DbName;
        var ctx = Ctx(caller: "anonymous");
        ctx.Vars["cb_num"] = "+15559998888";

        var node = Node(new JsonObject { ["numberSource"] = "collected", ["collectedVar"] = "cb_num" });
        var result = await NewHandler(dbName).ExecuteAsync(node, ctx);

        Assert.Equal("requested", result.TransitionTaken);

        await using var check = Db(dbName);
        var cb = Assert.Single(check.Callbacks);
        Assert.Equal("+15559998888", cb.CallbackNumber);
    }

    [Fact]
    public async Task CollectedNumberSource_MissingVar_FollowsFailed()
    {
        var dbName = DbName;
        var ctx = Ctx(caller: "anonymous");

        var node = Node(new JsonObject { ["numberSource"] = "collected", ["collectedVar"] = "cb_num" });
        var result = await NewHandler(dbName).ExecuteAsync(node, ctx);

        Assert.Equal("failed", result.TransitionTaken);
        await using var check = Db(dbName);
        Assert.Empty(check.Callbacks);
    }

    [Fact]
    public async Task CallerIdOverride_LiteralAndVariable_FrozenToLiteralOnTheRow()
    {
        var dbName = DbName;
        var ctx = Ctx();
        ctx.Vars["client_did"] = "+15035559999";

        // literal
        var n1 = Node(new JsonObject { ["callerIdOverride"] = "+18005551212" });
        await NewHandler(dbName).ExecuteAsync(n1, ctx);
        await using (var check = Db(dbName))
            Assert.Equal("+18005551212", (await check.Callbacks.SingleAsync()).CallerIdOverride);

        // {{variable}} resolved at request time
        var dbName2 = DbName;
        var n2 = Node(new JsonObject { ["callerIdOverride"] = "{{flow.client_did}}" });
        await NewHandler(dbName2).ExecuteAsync(n2, ctx);
        await using (var check2 = Db(dbName2))
            Assert.Equal("+15035559999", (await check2.Callbacks.SingleAsync()).CallerIdOverride);
    }

    [Fact]
    public async Task NoCallerIdOverride_LeavesRowNull()
    {
        var dbName = DbName;
        await NewHandler(dbName).ExecuteAsync(Node(), Ctx());
        await using var check = Db(dbName);
        Assert.Null((await check.Callbacks.SingleAsync()).CallerIdOverride);
    }
}
