using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>
/// CRM-script trigger_telephony_event node — lets an agent's script fire a named custom event on
/// the telephony flow bridged to the live call (e.g. kick off tf_secure_collect mid-call). Always
/// fire-and-continue: advances to "default" regardless of whether a live session/handler exists or
/// FireEventAsync throws.
/// </summary>
public class TriggerTelephonyEventNodeHandlerTests
{
    private static readonly Guid CallId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private readonly Mock<ITelephonyCallSessionStore> _sessionStore = new();
    private readonly Mock<ITelephonyFlowEngine> _telephonyEngine = new();
    private readonly Mock<ISharedCallVariableStore> _sharedVars = new();
    private readonly Dictionary<string, string> _storedShared = [];

    public TriggerTelephonyEventNodeHandlerTests() =>
        _sharedVars.Setup(s => s.GetAllAsync(CallId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Dictionary<string, string>(_storedShared));

    private TriggerTelephonyEventNodeHandler NewHandler() => new(
        new VariableResolver(), _sessionStore.Object, _telephonyEngine.Object, _sharedVars.Object,
        NullLogger<TriggerTelephonyEventNodeHandler>.Instance);

    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = CallId, InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_trigger",
    };

    private static JsonObject Node(string? eventName = "capture_card") => new()
    {
        ["type"]      = "trigger_telephony_event",
        ["label"]     = "Capture Card",
        ["eventName"] = eventName,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task MissingEventName_SkipsFiring_FollowsDefault()
    {
        var result = await NewHandler().ExecuteAsync(Node(eventName: null), Ctx(), agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        _sessionStore.Verify(s => s.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        _telephonyEngine.Verify(e => e.FireEventAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NoLiveSessionForCallRecord_SkipsFiring_FollowsDefault()
    {
        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await NewHandler().ExecuteAsync(Node(), Ctx(), agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        _telephonyEngine.Verify(e => e.FireEventAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LiveSessionFound_FiresCustomEventOnItsChannel_FollowsDefault()
    {
        var session = new TelephonyCallSession { ChannelUuid = "uuid-123", CallRecordId = CallId };
        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        _telephonyEngine.Setup(e => e.FireEventAsync(
                "uuid-123", "custom:capture_card", It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireEventResult { Handled = true });

        var ctx = Ctx();
        var result = await NewHandler().ExecuteAsync(Node(), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        _telephonyEngine.Verify(e => e.FireEventAsync(
            "uuid-123", "custom:capture_card",
            It.Is<FireEventContext>(c => c.AgentId == ctx.AgentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LiveSessionFound_StampsPendingWaitVars_ForTelEndNodeHandlerToNotifyLater()
    {
        var session = new TelephonyCallSession { ChannelUuid = "uuid-123", CallRecordId = CallId };
        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        _telephonyEngine.Setup(e => e.FireEventAsync(
                "uuid-123", "custom:capture_card", It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireEventResult { Handled = true });

        var ctx = Ctx();
        await NewHandler().ExecuteAsync(Node(), ctx, agentInput: null, agentTransition: "");

        _telephonyEngine.Verify(e => e.FireEventAsync(
            "uuid-123", "custom:capture_card",
            It.Is<FireEventContext>(c =>
                c.AdditionalVars["_trig_wait_event_name"] == "capture_card" &&
                c.AdditionalVars["_trig_wait_agent_id"] == ctx.AgentId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FireEventThrows_StillFollowsDefault()
    {
        var session = new TelephonyCallSession { ChannelUuid = "uuid-123", CallRecordId = CallId };
        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        _telephonyEngine.Setup(e => e.FireEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await NewHandler().ExecuteAsync(Node(), Ctx(), agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
    }

    [Fact]
    public async Task IgnoresSessionsForOtherCallRecords()
    {
        var other = new TelephonyCallSession { ChannelUuid = "uuid-other", CallRecordId = Guid.NewGuid() };
        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([other]);

        var result = await NewHandler().ExecuteAsync(Node(), Ctx(), agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        _telephonyEngine.Verify(e => e.FireEventAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // S176 regression: the fired branch can finish inside FireEventAsync and write {{shared.*}} (CS transfer:
    // time-of-day → set shared.outside_hours → tf_end). CRM nodes that auto-advance right after this one must see
    // those values, not the snapshot loaded when the advance began.
    [Fact]
    public async Task ReloadsSharedVariables_WrittenByTheEventBranch()
    {
        _storedShared["outside_hours"] = "false";
        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelephonyCallSession { ChannelUuid = "uuid-123", CallRecordId = CallId }]);
        _telephonyEngine.Setup(e => e.FireEventAsync(
                "uuid-123", "custom:capture_card", It.IsAny<FireEventContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => _storedShared["outside_hours"] = "true")
            .ReturnsAsync(new FireEventResult { Handled = true });

        var ctx = Ctx();
        ctx.SharedVars = new Dictionary<string, string> { ["outside_hours"] = "false" };   // loaded at advance start
        await NewHandler().ExecuteAsync(Node(), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("true", ctx.SharedVars["outside_hours"]);
    }
}
