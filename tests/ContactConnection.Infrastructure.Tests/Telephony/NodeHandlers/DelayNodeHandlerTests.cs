using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_delay — pauses the flow for a configured duration via a deferred continuation
/// (uuid_transfer into delay_wait; EslBackgroundService.HandleDelayDoneAsync drives the resume).
/// This handler only kicks the wait off; covers duration resolution (literal + {{variable}}),
/// the zero/invalid-duration fast path, the max-delay clamp, and the state it stashes before
/// suspending.
/// </summary>
public class DelayNodeHandlerTests
{
    private const string Uuid = "call-uuid-delay";

    private sealed class Harness
    {
        public Mock<IEslCommander> Esl { get; } = new();
        public Mock<ITelephonyCallSessionStore> SessionStore { get; } = new();
        public DelayNodeHandler Handler { get; }

        public Harness()
        {
            SessionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TelephonyCallSession?)null);

            var eslFactory = new Mock<IEslCommanderFactory>(); // unused — ctx.Esl is supplied

            Handler = new DelayNodeHandler(SessionStore.Object, eslFactory.Object, NullLogger<DelayNodeHandler>.Instance);
        }
    }

    private static TelephonyFlowContext Ctx(IEslCommander esl, params (string k, string v)[] vars)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
            Esl = esl,
        };
        foreach (var (k, v) in vars) ctx.Vars[k] = v;
        return ctx;
    }

    private static JsonObject Node(string? durationMs) => new()
    {
        ["type"] = "tf_delay",
        ["nodeId"] = "tf_delay_1",
        ["delayDurationMs"] = durationMs,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task PositiveLiteralDuration_StashesState_SetsChannelVar_Transfers_Suspends()
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object);

        var result = await h.Handler.ExecuteAsync(Node("2000"), ctx);

        Assert.Equal("waiting", result.TransitionTaken);
        Assert.Null(result.NextNodeId); // suspended — EslBackgroundService resumes via delay_done

        Assert.Equal("true", ctx.Vars["_delay_in_progress"]);
        Assert.Equal("n_next", ctx.Vars["_delay_next_node_id"]);

        h.Esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_delay_ms", "2000", It.IsAny<CancellationToken>()), Times.Once);
        h.Esl.Verify(e => e.TransferAsync(Uuid, "delay_wait", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VariableResolvedDuration_ResolvesBeforeUse()
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object, ("wait_ms", "1500"));

        await h.Handler.ExecuteAsync(Node("{{flow.wait_ms}}"), ctx);

        h.Esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_delay_ms", "1500", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public async Task NonPositiveOrUnparseableDuration_SkipsWait_GoesStraightToDefault(string? duration)
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object);

        var result = await h.Handler.ExecuteAsync(Node(duration), ctx);

        Assert.Equal("default", result.TransitionTaken);
        Assert.Equal("n_next", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_delay_in_progress"));
        h.Esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DurationAboveCap_Clamped()
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object);

        await h.Handler.ExecuteAsync(Node("999999999"), ctx);

        h.Esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_delay_ms", "300000", It.IsAny<CancellationToken>()), Times.Once);
    }
}
