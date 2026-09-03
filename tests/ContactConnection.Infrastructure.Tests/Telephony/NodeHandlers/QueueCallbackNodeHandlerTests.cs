using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_queue_callback ("virtual hold") — opts a queued caller into a callback that keeps their
/// queue position. The node only marks the session; delivery is QueuePollingService +
/// QueueCallbackDeliveryService.
/// </summary>
public class QueueCallbackNodeHandlerTests
{
    private const string Uuid = "call-uuid-qcb";
    private static readonly Guid CallId     = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid TenantId   = Guid.Parse("dddddddd-0000-0000-0000-0000000000aa");
    private static readonly Guid CampaignId = Guid.Parse("dddddddd-0000-0000-0000-0000000000bb");

    private readonly Mock<ICallStateHistoryRecorder> _stateRecorder = new();
    private readonly Mock<ITelephonyCallSessionStore> _sessionStore = new();

    private QueueCallbackNodeHandler NewHandler(TelephonyCallSession? session = null)
    {
        _sessionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        return new QueueCallbackNodeHandler(
            _stateRecorder.Object, _sessionStore.Object, NullLogger<QueueCallbackNodeHandler>.Instance);
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
        ctx.Vars["_in_queue_at"] = DateTimeOffset.UtcNow.AddSeconds(-30).ToString("O");
        return ctx;
    }

    private static JsonObject Node(JsonObject? overrides = null)
    {
        var n = new JsonObject
        {
            ["type"] = "tf_queue_callback",
            ["numberSource"] = "ani",
            ["maxAttempts"] = 2,
            ["transitions"] = new JsonObject { ["queued"] = "n_hangup", ["failed"] = "n_back", ["default"] = "n_hangup" },
        };
        if (overrides is not null)
            foreach (var kv in overrides) n[kv.Key] = kv.Value?.DeepClone();
        return n;
    }

    [Fact]
    public async Task ValidAni_MarksPlaceholder_KeepsQueuePosition_FollowsQueued()
    {
        var ctx = Ctx();
        var inQueueAt = ctx.Vars["_in_queue_at"];

        var result = await NewHandler().ExecuteAsync(Node(), ctx);

        Assert.Equal("queued", result.TransitionTaken);
        Assert.Equal("n_hangup", result.NextNodeId);

        Assert.Equal("true", ctx.Vars["_queue_callback"]);
        Assert.Equal("+15551110000", ctx.Vars["_queue_callback_number"]);
        Assert.Equal("2", ctx.Vars["_queue_callback_max_attempts"]);
        Assert.Equal("0", ctx.Vars["_queue_callback_attempts"]);
        Assert.Equal("true", ctx.Vars["_left_for_callback"]);

        // Position preserved — _queued and _in_queue_at untouched.
        Assert.Equal("true", ctx.Vars["_queued"]);
        Assert.Equal(inQueueAt, ctx.Vars["_in_queue_at"]);

        _stateRecorder.Verify(r => r.RecordAsync(
            TenantId, "tenant_test_tenant", CallId, CallHistoryState.PostAgent, CampaignId,
            null, It.Is<string>(s => s.Contains("Queue callback requested")),
            null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithheldAni_NoCollectedVar_FollowsFailed_DoesNotMark()
    {
        var ctx = Ctx(caller: "anonymous");

        var result = await NewHandler().ExecuteAsync(Node(), ctx);

        Assert.Equal("failed", result.TransitionTaken);
        Assert.Equal("n_back", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_queue_callback"));
    }

    [Fact]
    public async Task CollectedNumberSource_ReadsSessionVar()
    {
        var ctx = Ctx(caller: "anonymous");
        ctx.Vars["cb_num"] = "+15559998888";
        var node = Node(new JsonObject { ["numberSource"] = "collected", ["collectedVar"] = "cb_num" });

        var result = await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("queued", result.TransitionTaken);
        Assert.Equal("+15559998888", ctx.Vars["_queue_callback_number"]);
    }

    [Fact]
    public async Task PersistsMarkersToSessionDirectly()
    {
        var session = new TelephonyCallSession
        {
            ChannelUuid = Uuid,
            Vars = new Dictionary<string, string> { ["_queued"] = "true" },
        };

        await NewHandler(session).ExecuteAsync(Node(), Ctx());

        Assert.Equal("true", session.Vars["_queue_callback"]);
        Assert.Equal("+15551110000", session.Vars["_queue_callback_number"]);
        Assert.Equal("true", session.Vars["_queued"]);
        _sessionStore.Verify(s => s.SaveAsync(session, It.IsAny<CancellationToken>()), Times.Once);
    }
}
