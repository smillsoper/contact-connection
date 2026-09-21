using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class RejectNodeHandlerTests
{
    private static readonly RejectNodeHandler Handler = new();

    private static TelephonyFlowContext Ctx(IEslCommander esl) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.KillChannelAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return esl;
    }

    [Theory]
    [InlineData("busy", 17)]
    [InlineData(null, 17)]           // default when omitted
    [InlineData("unavailable", 19)]
    [InlineData("declined", 21)]
    [InlineData("something_unknown", 17)] // falls back to busy
    public async Task MapsCauseToCorrectQ850Code(string? cause, int expectedCode)
    {
        var esl = NewEsl();
        var node = new JsonObject { ["type"] = "tf_reject", ["cause"] = cause };

        var result = await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.KillChannelAsync("uuid-1", expectedCode, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(result.NextNodeId);
        Assert.Equal("rejected", result.TransitionTaken);
    }
}
