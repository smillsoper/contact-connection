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
/// A tf_ivr_menu(alwaysListen=true) hot-digit listener must be superseded the moment the flow
/// enters an actual synchronous DTMF capture — tf_secure_collect always, or a plain (non-async)
/// tf_ivr_menu — so it can never compete with a real blocking prompt on the same channel. Cleared
/// centrally in TelephonyFlowEngine's node dispatch (not per-handler) so this holds for every
/// synchronous capture node type, not just the ones that existed when this was written.
/// </summary>
public class TelephonyFlowEngineHotDigitClearTests
{
    private sealed class NoopHandler(string nodeType) : ITelephonyNodeHandler
    {
        public string NodeType { get; } = nodeType;
        public Task<TelephonyNodeResult> ExecuteAsync(JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default) =>
            Task.FromResult(new TelephonyNodeResult(null, "done"));
    }

    [Fact]
    public async Task EnteringSecureCollect_ClearsArmedHotDigitListener()
    {
        var session = new TelephonyCallSession
        {
            ChannelUuid        = "hd-uuid-1",
            FlowId             = Guid.NewGuid(),
            FlowDefinitionJson = new JsonObject
            {
                ["nodes"] = new JsonObject
                {
                    ["n1"] = new JsonObject { ["type"] = "tf_secure_collect", ["transitions"] = new JsonObject() },
                },
            }.ToJsonString(),
            Vars = new Dictionary<string, string>
            {
                ["_hot_digit_options"] = "{\"1\":\"node_x\"}",
                ["_hot_digit_node_id"] = "tf_ivr_menu_hot",
            },
        };

        var store = new Mock<ITelephonyCallSessionStore>();
        store.Setup(s => s.GetAsync("hd-uuid-1", It.IsAny<CancellationToken>())).ReturnsAsync(session);
        TelephonyCallSession? saved = null;
        store.Setup(s => s.SaveAsync(It.IsAny<TelephonyCallSession>(), It.IsAny<CancellationToken>()))
            .Callback<TelephonyCallSession, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);

        var engine = new TelephonyFlowEngine(
            Mock.Of<ITenantDbContextFactory>(),
            store.Object,
            new ITelephonyNodeHandler[] { new NoopHandler("tf_secure_collect") },
            Mock.Of<ICallTraceRecorder>(),
            Mock.Of<ICallTraceSubscriptionRegistry>(),
            Mock.Of<ICallTraceNotifier>(),
            NullLogger<TelephonyFlowEngine>.Instance);

        await engine.ResumeFromNodeAsync("hd-uuid-1", "n1", Mock.Of<IEslCommander>());

        Assert.NotNull(saved);
        Assert.False(saved!.Vars.ContainsKey("_hot_digit_options"));
        Assert.False(saved.Vars.ContainsKey("_hot_digit_node_id"));
    }

    [Fact]
    public async Task EnteringSyncIvrMenu_ClearsArmedHotDigitListener()
    {
        var session = new TelephonyCallSession
        {
            ChannelUuid        = "hd-uuid-2",
            FlowId             = Guid.NewGuid(),
            FlowDefinitionJson = new JsonObject
            {
                ["nodes"] = new JsonObject
                {
                    ["n1"] = new JsonObject { ["type"] = "tf_ivr_menu", ["transitions"] = new JsonObject() },
                },
            }.ToJsonString(),
            Vars = new Dictionary<string, string>
            {
                ["_hot_digit_options"] = "{\"1\":\"node_x\"}",
                ["_hot_digit_node_id"] = "tf_ivr_menu_hot",
            },
        };

        var store = new Mock<ITelephonyCallSessionStore>();
        store.Setup(s => s.GetAsync("hd-uuid-2", It.IsAny<CancellationToken>())).ReturnsAsync(session);
        TelephonyCallSession? saved = null;
        store.Setup(s => s.SaveAsync(It.IsAny<TelephonyCallSession>(), It.IsAny<CancellationToken>()))
            .Callback<TelephonyCallSession, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);

        var engine = new TelephonyFlowEngine(
            Mock.Of<ITenantDbContextFactory>(),
            store.Object,
            new ITelephonyNodeHandler[] { new NoopHandler("tf_ivr_menu") },
            Mock.Of<ICallTraceRecorder>(),
            Mock.Of<ICallTraceSubscriptionRegistry>(),
            Mock.Of<ICallTraceNotifier>(),
            NullLogger<TelephonyFlowEngine>.Instance);

        await engine.ResumeFromNodeAsync("hd-uuid-2", "n1", Mock.Of<IEslCommander>());

        Assert.NotNull(saved);
        Assert.False(saved!.Vars.ContainsKey("_hot_digit_options"));
        Assert.False(saved.Vars.ContainsKey("_hot_digit_node_id"));
    }

    [Fact]
    public async Task EnteringAsyncHotDigitIvrMenu_DoesNotClearItself()
    {
        // alwaysListen=true is arming, not competing — must not wipe out the listener it's about
        // to (re)arm on entry, nor any listener a different node just armed.
        var session = new TelephonyCallSession
        {
            ChannelUuid        = "hd-uuid-3",
            FlowId             = Guid.NewGuid(),
            FlowDefinitionJson = new JsonObject
            {
                ["nodes"] = new JsonObject
                {
                    ["n1"] = new JsonObject
                    {
                        ["type"] = "tf_ivr_menu",
                        ["alwaysListen"] = true,
                        ["transitions"] = new JsonObject(),
                    },
                },
            }.ToJsonString(),
            Vars = new Dictionary<string, string>
            {
                ["_hot_digit_options"] = "{\"1\":\"node_x\"}",
                ["_hot_digit_node_id"] = "some_other_node",
            },
        };

        var store = new Mock<ITelephonyCallSessionStore>();
        store.Setup(s => s.GetAsync("hd-uuid-3", It.IsAny<CancellationToken>())).ReturnsAsync(session);
        TelephonyCallSession? saved = null;
        store.Setup(s => s.SaveAsync(It.IsAny<TelephonyCallSession>(), It.IsAny<CancellationToken>()))
            .Callback<TelephonyCallSession, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);

        var engine = new TelephonyFlowEngine(
            Mock.Of<ITenantDbContextFactory>(),
            store.Object,
            new ITelephonyNodeHandler[] { new NoopHandler("tf_ivr_menu") },
            Mock.Of<ICallTraceRecorder>(),
            Mock.Of<ICallTraceSubscriptionRegistry>(),
            Mock.Of<ICallTraceNotifier>(),
            NullLogger<TelephonyFlowEngine>.Instance);

        await engine.ResumeFromNodeAsync("hd-uuid-3", "n1", Mock.Of<IEslCommander>());

        Assert.NotNull(saved);
        Assert.True(saved!.Vars.ContainsKey("_hot_digit_options"));
    }
}
