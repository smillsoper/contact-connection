using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class IvrMenuNodeHandlerTests
{
    private const string Uuid = "call-uuid-1";
    private const string BuiltinPrompt = "__builtin:/usr/share/freeswitch/sounds/menu.wav";

    private static IvrMenuNodeHandler NewHandler()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new IvrMenuNodeHandler(
            new Mock<ITenantDbContextFactory>().Object, config, NullLogger<IvrMenuNodeHandler>.Instance);
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static JsonObject MenuNode() => new()
    {
        ["type"] = "tf_ivr_menu",
        ["promptAudioFileId"] = BuiltinPrompt,
        ["maxDigits"] = 1,
        ["maxTries"] = 3,
        ["timeoutMs"] = 5000,
        ["interDigitTimeoutMs"] = 3000,
        ["options"] = new JsonArray
        {
            new JsonObject { ["digit"] = "1", ["transition"] = "sales" },
            new JsonObject { ["digit"] = "2", ["transition"] = "support" },
        },
        ["transitions"] = new JsonObject
        {
            ["sales"] = "node_sales",
            ["support"] = "node_support",
            ["no_match"] = "node_operator",
        },
    };

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    [Fact]
    public async Task HappyPath_SetsIvrVars_TransfersToIvrCollect_AndStoresContinuation()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(MenuNode(), ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("collecting", result.TransitionTaken);

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_min", "1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_max", "1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_tries", "3", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_term", "none", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_prompt", "/usr/share/freeswitch/sounds/menu.wav", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_invalid", "silence_stream://250", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_regex", "^(1|2)$", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_digit_timeout", "3000", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "ivr_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("true", ctx.Vars["_ivr_in_progress"]);
        Assert.Equal("node_operator", ctx.Vars["_ivr_no_match"]);
        Assert.Contains("\"1\":\"node_sales\"", ctx.Vars["_ivr_options"]);
        Assert.Contains("\"2\":\"node_support\"", ctx.Vars["_ivr_options"]);
    }

    [Fact]
    public async Task MultiDigit_DefaultsTerminatorToHash()
    {
        var esl = NewEsl();
        var node = MenuNode();
        node["maxDigits"] = 4;
        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_term", "#", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoEsl_FollowsNoMatchTransition()
    {
        var result = await NewHandler().ExecuteAsync(MenuNode(), Ctx(esl: null));
        Assert.Equal("node_operator", result.NextNodeId);
        Assert.Equal("no_match", result.TransitionTaken);
    }

    [Fact]
    public async Task NoPromptAudio_FollowsNoMatch_AndDoesNotTransfer()
    {
        var esl = NewEsl();
        var node = MenuNode();
        node.Remove("promptAudioFileId");
        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_operator", result.NextNodeId);
        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnwiredOption_IsOmittedFromOptionMap()
    {
        var esl = NewEsl();
        var node = MenuNode();
        ((JsonObject)node["transitions"]!).Remove("support");
        var ctx = Ctx(esl.Object);
        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Contains("\"1\":\"node_sales\"", ctx.Vars["_ivr_options"]);
        Assert.DoesNotContain("\"2\"", ctx.Vars["_ivr_options"]);
    }
}
