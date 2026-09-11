using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// AnswerNodeHandler's lead-in silence priming — after answering, it plays a short silence burst
/// (default 300 ms, override via node["leadInSilenceMs"], 0 disables) and marks the channel
/// primed, so the first syllable of the next prompt isn't clipped while the far end ramps its
/// jitter buffer.
/// </summary>
public class AnswerNodeHandlerTests
{
    private static TelephonyFlowContext NewContext(Mock<IEslCommander> esl) => new()
    {
        ChannelUuid       = "chan-1",
        CallerNumber      = "+15551234567",
        DestinationNumber = "+15559876543",
        TenantId          = Guid.NewGuid(),
        CampaignId        = Guid.NewGuid(),
        CallRecordId      = Guid.NewGuid(),
        TenantSubdomain   = "test-tenant",
        TenantSchemaName  = "tenant_test_tenant",
        TenantTimezone    = "America/Chicago",
        Esl               = esl.Object,
    };

    [Fact]
    public async Task Default_PrimesWith300msSilence_AndMarksChannelPrimed()
    {
        var esl = new Mock<IEslCommander>();
        var ctx = NewContext(esl);
        var node = new JsonObject { ["transitions"] = new JsonObject { ["default"] = "next" } };

        var result = await new AnswerNodeHandler().ExecuteAsync(node, ctx);

        esl.Verify(e => e.AnswerChannelAsync("chan-1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync("chan-1", "silence_stream://300,0", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("true", ctx.Vars["_media_primed"]);
        Assert.Equal("next", result.NextNodeId);
    }

    /// <summary>
    /// Every leg is bridged/re-bridged via the ESL uuid_bridge API (not the dialplan bridge() app),
    /// whose default teardown hangs BOTH legs up the instant either one leaves the bridge. Without
    /// park_after_bridge=true, pulling one leg out mid-bridge (e.g. tf_secure_collect parking the
    /// agent leg for a mid-call capture) kills the other leg too — reproduced live on a real bridged
    /// call (S135): the caller leg was already gone ("No such channel") by the time
    /// SecureCollectNodeHandler tried to uuid_transfer it into the capture extension.
    /// </summary>
    [Fact]
    public async Task Always_SetsParkAfterBridge_SoAPeerLeavingTheBridgeDoesNotHangUpThisLeg()
    {
        var esl = new Mock<IEslCommander>();
        var ctx = NewContext(esl);
        var node = new JsonObject { ["transitions"] = new JsonObject { ["default"] = "next" } };

        await new AnswerNodeHandler().ExecuteAsync(node, ctx);

        esl.Verify(e => e.SetChannelVarAsync("chan-1", "park_after_bridge", "true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeadInZero_SkipsSilenceBroadcast_AndDoesNotMarkPrimed()
    {
        var esl = new Mock<IEslCommander>();
        var ctx = NewContext(esl);
        var node = new JsonObject { ["leadInSilenceMs"] = 0 };

        await new AnswerNodeHandler().ExecuteAsync(node, ctx);

        esl.Verify(e => e.AnswerChannelAsync("chan-1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(ctx.Vars.ContainsKey("_media_primed"));
    }

    [Fact]
    public async Task CustomLeadIn_UsesConfiguredDuration()
    {
        var esl = new Mock<IEslCommander>();
        var ctx = NewContext(esl);
        var node = new JsonObject { ["leadInSilenceMs"] = 500 };

        await new AnswerNodeHandler().ExecuteAsync(node, ctx);

        esl.Verify(e => e.BroadcastAsync("chan-1", "silence_stream://500,0", It.IsAny<CancellationToken>()), Times.Once);
    }
}
