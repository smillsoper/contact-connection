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

    private TriggerTelephonyEventNodeHandler NewHandler() => new(
        new VariableResolver(), _sessionStore.Object, _telephonyEngine.Object, NullLogger<TriggerTelephonyEventNodeHandler>.Instance);

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
}
