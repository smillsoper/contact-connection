using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.CallTrace;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// ResumeFromNodeAsync (PLAYBACK_STOP / ivr_done continuation) re-saves the engine's own copy of
/// the session after the node runs. A handler that removes a key from ctx.Vars alone has that
/// removal silently undone — it must use ctx.RemoveSessionVar so the engine deletes the key on
/// its post-node sync. Regression guard for tf_scheduled_callback leaving a booked caller
/// still _queued (and therefore still deliverable to an agent) when it fired from an IVR branch.
/// </summary>
public class TelephonyFlowEngineResumeTests
{
    private sealed class VarRemovingHandler : ITelephonyNodeHandler
    {
        public string NodeType => "test_remove_queued";
        public Task<TelephonyNodeResult> ExecuteAsync(JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
        {
            ctx.RemoveSessionVar("_queued");
            ctx.Vars["_left_for_callback"] = "true";
            return Task.FromResult(new TelephonyNodeResult(null, "done"));
        }
    }

    [Fact]
    public async Task ResumeFromNode_HandlerRemoveSessionVar_DeletesKeyFromPersistedSession()
    {
        const string uuid = "resume-uuid-1";
        var flowDef = new JsonObject
        {
            ["nodes"] = new JsonObject
            {
                ["n1"] = new JsonObject { ["type"] = "test_remove_queued", ["transitions"] = new JsonObject() },
            },
        }.ToJsonString();

        var session = new TelephonyCallSession
        {
            ChannelUuid        = uuid,
            FlowId             = Guid.NewGuid(),
            FlowDefinitionJson = flowDef,
            Vars               = new Dictionary<string, string> { ["_queued"] = "true", ["_in_queue_at"] = "x" },
        };

        var store = new Mock<ITelephonyCallSessionStore>();
        store.Setup(s => s.GetAsync(uuid, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        TelephonyCallSession? saved = null;
        store.Setup(s => s.SaveAsync(It.IsAny<TelephonyCallSession>(), It.IsAny<CancellationToken>()))
            .Callback<TelephonyCallSession, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);

        var engine = new TelephonyFlowEngine(
            Mock.Of<ITenantDbContextFactory>(),
            store.Object,
            new ITelephonyNodeHandler[] { new VarRemovingHandler() },
            Mock.Of<ICallTraceRecorder>(),
            Mock.Of<ICallTraceSubscriptionRegistry>(),
            Mock.Of<ICallTraceNotifier>(),
            NullLogger<TelephonyFlowEngine>.Instance);

        await engine.ResumeFromNodeAsync(uuid, "n1", Mock.Of<IEslCommander>());

        Assert.NotNull(saved);
        Assert.False(saved!.Vars.ContainsKey("_queued"));
        Assert.Equal("true", saved.Vars["_left_for_callback"]);
    }
}
