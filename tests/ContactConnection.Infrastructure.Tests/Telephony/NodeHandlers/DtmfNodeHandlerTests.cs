using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class DtmfNodeHandlerTests
{
    private static DtmfNodeHandler NewHandler() => new(NullLogger<DtmfNodeHandler>.Instance);

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static JsonObject Node(string digits, int? durationMs = null, int? interDigitGapMs = null, bool waitForCompletion = true) => new()
    {
        ["type"] = "tf_dtmf",
        ["digits"] = digits,
        ["durationMs"] = durationMs,
        ["interDigitGapMs"] = interDigitGapMs,
        ["waitForCompletion"] = waitForCompletion,
        ["transitions"] = new JsonObject { ["default"] = "node_next" },
    };

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SendDtmfAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    [Fact]
    public async Task SendsEachDigit_InOrder_AndFollowsDefault()
    {
        var esl = NewEsl();
        var result = await NewHandler().ExecuteAsync(Node("123", durationMs: 10, interDigitGapMs: 0), Ctx(esl.Object));

        Assert.Equal("node_next", result.NextNodeId);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "1", 10, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "2", 10, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "3", 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StripsInvalidCharacters_KeepsOnlyValidDtmfSet()
    {
        var esl = NewEsl();
        // Letters E/F, punctuation, and spaces are invalid; 0-9 * # A-D w W are valid.
        await NewHandler().ExecuteAsync(Node("1a2*b#EF-3", durationMs: 5, interDigitGapMs: 0), Ctx(esl.Object));

        esl.Verify(e => e.SendDtmfAsync("uuid-1", "1", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "2", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "*", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "#", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "3", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "a", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "E", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "-", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VariableSubstitution_ReplacesTokenBeforeSending()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        ctx.Vars["pin"] = "4477";

        await NewHandler().ExecuteAsync(Node("{{pin}}#", durationMs: 5, interDigitGapMs: 0), ctx);

        esl.Verify(e => e.SendDtmfAsync("uuid-1", "4", 5, It.IsAny<CancellationToken>()), Times.Exactly(2));
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "7", 5, It.IsAny<CancellationToken>()), Times.Exactly(2));
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "#", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VariableSubstitution_SupportsNamespacedTags_NotJustBareVars()
    {
        // Before this fix, DtmfNodeHandler had its own hand-rolled substitution loop that only
        // matched raw ctx.Vars keys — {{caller.ani}}/{{call.did}}/{{shared.x}}/{{now.*}} silently
        // passed through unresolved. Now it shares TelSetVariableNodeHandler.Resolve, the same
        // resolver every other value field in the telephony designer uses.
        var esl = NewEsl();
        var ctx = Ctx(esl.Object); // CallerNumber = "+15551234567"

        await NewHandler().ExecuteAsync(Node("{{caller.ani}}", durationMs: 5, interDigitGapMs: 0), ctx);

        esl.Verify(e => e.SendDtmfAsync("uuid-1", "1", 5, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "5", 5, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task NoValidDigitsAfterStripping_SendsNothing_StillFollowsDefault()
    {
        var esl = NewEsl();
        var result = await NewHandler().ExecuteAsync(Node("!!!", durationMs: 5), Ctx(esl.Object));

        Assert.Equal("node_next", result.NextNodeId);
        esl.Verify(e => e.SendDtmfAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoEsl_FollowsDefault_NoThrow()
    {
        var result = await NewHandler().ExecuteAsync(Node("123"), Ctx(esl: null));
        Assert.Equal("node_next", result.NextNodeId);
    }

    [Fact]
    public async Task PauseCharacters_DoNotReachSendDtmf()
    {
        var esl = NewEsl();
        await NewHandler().ExecuteAsync(Node("1wW2", durationMs: 5, interDigitGapMs: 0), Ctx(esl.Object));

        esl.Verify(e => e.SendDtmfAsync("uuid-1", "1", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync("uuid-1", "2", 5, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SendDtmfAsync(It.IsAny<string>(), "w", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.SendDtmfAsync(It.IsAny<string>(), "W", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
