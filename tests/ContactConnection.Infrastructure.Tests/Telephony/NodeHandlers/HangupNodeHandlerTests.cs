using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class HangupNodeHandlerTests
{
    private static readonly HangupNodeHandler Handler = new();

    private static TelephonyFlowContext Ctx(IEslCommander esl) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    [Fact]
    public async Task HangsUpChannel_TerminalWithNoNextNode()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.HangupChannelAsync("uuid-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Handler.ExecuteAsync(new JsonObject { ["type"] = "tf_hangup" }, Ctx(esl.Object));

        esl.Verify(e => e.HangupChannelAsync("uuid-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(result.NextNodeId);
        Assert.Equal("hangup", result.TransitionTaken);
    }
}
