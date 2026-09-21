using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class SetCallerIdNodeHandlerTests
{
    private static readonly SetCallerIdNodeHandler Handler = new();

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
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    [Fact]
    public async Task LiteralValue_SetsEffectiveCallerIdNumber()
    {
        var esl = NewEsl();
        var node = new JsonObject { ["type"] = "tf_set_caller_id", ["callerIdValue"] = "+15035551234", ["transitions"] = new JsonObject { ["default"] = "n_next" } };

        var result = await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync("uuid-1", "effective_caller_id_number", "+15035551234", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("n_next", result.NextNodeId);
    }

    [Fact]
    public async Task TemplateValue_ResolvesBeforeSetting()
    {
        var esl = NewEsl();
        var node = new JsonObject { ["type"] = "tf_set_caller_id", ["callerIdValue"] = "{{caller.ani}}" };

        await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync("uuid-1", "effective_caller_id_number", "+15551234567", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyValue_DoesNotCallEsl()
    {
        var esl = NewEsl();
        var node = new JsonObject { ["type"] = "tf_set_caller_id", ["callerIdValue"] = "" };

        await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TemplateResolvesToEmpty_DoesNotCallEsl()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        // {{flow.unset_var}} resolves to "" — guard should skip the ESL call entirely rather than
        // blanking the channel's effective caller ID.
        var node = new JsonObject { ["type"] = "tf_set_caller_id", ["callerIdValue"] = "{{flow.unset_var}}" };

        await Handler.ExecuteAsync(node, ctx);

        esl.Verify(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
