using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// The designer previously wrote this node's config under "sipHeaderName"/"sipHeaderValue" while
/// the handler here reads "headerName"/"value" — a property-name mismatch (same bug class as the
/// tf_secure_collect "secureFields"/"fields" one) that made every tf_set_sip_header node built
/// through the visual designer a silent no-op. Fixed by aligning the designer to these key names;
/// this file pins down the handler's actual contract so it can't regress from the other side.
/// </summary>
public class SetSipHeaderNodeHandlerTests
{
    private static readonly SetSipHeaderNodeHandler Handler = new();

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
    public async Task SetsSipHeaderChannelVar_PrefixedWithSipH()
    {
        var esl = NewEsl();
        var node = new JsonObject
        {
            ["type"] = "tf_set_sip_header",
            ["headerName"] = "Contact-ID",
            ["value"] = "abc-123",
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync("uuid-1", "sip_h_Contact-ID", "abc-123", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("n_next", result.NextNodeId);
    }

    [Fact]
    public async Task Value_ResolvesTemplateTagsBeforeSetting()
    {
        var esl = NewEsl();
        var node = new JsonObject
        {
            ["type"] = "tf_set_sip_header",
            ["headerName"] = "X-Custom-Ani",
            ["value"] = "{{caller.ani}}",
        };

        await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync("uuid-1", "sip_h_X-Custom-Ani", "+15551234567", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoHeaderName_DoesNotCallEsl()
    {
        var esl = NewEsl();
        var node = new JsonObject { ["type"] = "tf_set_sip_header", ["value"] = "abc-123" };

        await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoValue_SetsEmptyStringAsTheHeaderValue()
    {
        var esl = NewEsl();
        var node = new JsonObject { ["type"] = "tf_set_sip_header", ["headerName"] = "X-Empty" };

        await Handler.ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync("uuid-1", "sip_h_X-Empty", "", It.IsAny<CancellationToken>()), Times.Once);
    }
}
