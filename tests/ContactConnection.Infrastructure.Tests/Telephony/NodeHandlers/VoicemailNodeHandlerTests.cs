using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class VoicemailNodeHandlerTests
{
    private const string Uuid = "call-uuid-vm";

    private static VoicemailNodeHandler NewHandler()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FreeSWITCH:RecordingsContainerPath"] = "/var/lib/freeswitch/recordings",
        }).Build();
        return new VoicemailNodeHandler(
            new Mock<ITenantDbContextFactory>().Object,
            new Mock<ITtsStreamingService>().Object,
            new Mock<ITtsFileSynthesizer>().Object,
            config, NullLogger<VoicemailNodeHandler>.Instance);
    }

    private static readonly Guid CallId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = CallId,
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static JsonObject Node(JsonObject? overrides = null)
    {
        var n = new JsonObject
        {
            ["type"] = "tf_voicemail",
            ["nodeId"] = "tf_voicemail_1",
            ["maxLengthSeconds"] = 90,
            ["maxSilenceSeconds"] = 4,
            ["minLengthSeconds"] = 3,
            ["beepEnabled"] = true,
            ["transitions"] = new JsonObject
            {
                ["recorded"] = "node_after_vm",
                ["no_message"] = "node_no_msg",
                ["default"] = "node_after_vm",
            },
        };
        if (overrides is not null)
            foreach (var kv in overrides) n[kv.Key] = kv.Value?.DeepClone();
        return n;
    }

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
    public async Task NoEsl_FollowsNoMessage()
    {
        var result = await NewHandler().ExecuteAsync(Node(), Ctx(esl: null));
        Assert.Equal("no_message", result.TransitionTaken);
        Assert.Equal("node_no_msg", result.NextNodeId);
    }

    [Fact]
    public async Task HappyPath_SetsVmVars_TransfersToVmRecord_StoresContinuation()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(Node(), ctx);

        // terminal — resume happens from the vm_done event
        Assert.Null(result.NextNodeId);
        Assert.Equal("recording", result.TransitionTaken);

        esl.Verify(e => e.TransferAsync(Uuid, "vm_record", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_path",
            $"/var/lib/freeswitch/recordings/{CallId}-vm.wav", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_maxlen", "90", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_silence_secs", "4", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_beep", "tone_stream://%(500,0,800)", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("true", ctx.Vars["_vm_in_progress"]);
        Assert.Equal("tf_voicemail_1", ctx.Vars["_vm_node_id"]);
        Assert.Equal("node_after_vm", ctx.Vars["_vm_next_recorded"]);
        Assert.Equal("node_no_msg", ctx.Vars["_vm_next_no_message"]);
        Assert.Equal("3000", ctx.Vars["_vm_min_ms"]);
        Assert.Equal($"/var/lib/freeswitch/recordings/{CallId}-vm.wav", ctx.Vars["_vm_path"]);
    }

    [Fact]
    public async Task BeepDisabled_UsesSilenceForBeepSlot()
    {
        var esl = NewEsl();
        await NewHandler().ExecuteAsync(Node(new JsonObject { ["beepEnabled"] = false }), Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_beep", "silence_stream://100", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoGreetingConfigured_FallsBackToSilence()
    {
        var esl = NewEsl();
        await NewHandler().ExecuteAsync(Node(), Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_greeting", "silence_stream://500", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GreetingTts_WhenNoAudioFile_RoutesThroughFliteChannelVar()
    {
        var esl = NewEsl();
        var node = Node(new JsonObject
        {
            ["greetingTtsText"] = "You have reached support after hours. Leave a message.",
            ["greetingTtsVoice"] = "slt",
        });

        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_greeting_text",
            "You have reached support after hours. Leave a message.", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_greeting",
            "tts://flite|slt|${cc_vm_greeting_text}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamUriGreeting_PassesThroughUnchanged()
    {
        var esl = NewEsl();
        var node = Node(new JsonObject { ["greetingAudioFileId"] = "local_stream://moh" });
        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_vm_greeting", "local_stream://moh", It.IsAny<CancellationToken>()), Times.Once);
    }
}
