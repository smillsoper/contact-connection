using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>
/// CRM "script" node. Focused on the waitForTelephonyEventName field added to close the
/// trigger_telephony_event fire-and-continue race (project_shared_call_variables "known
/// follow-up") — the field is read straight off the node JSON by NodeHandlerBase.BuildState and
/// passed through unchanged to the agent UI, which decides what to do with it. No prior test
/// coverage existed for this handler.
/// </summary>
public class ScriptNodeHandlerTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_script",
    };

    private static JsonObject Node(string? waitForTelephonyEventName = null, int? waitForTelephonyEventTimeoutSeconds = null)
    {
        var node = new JsonObject
        {
            ["type"]        = "script",
            ["content"]     = "Wait for secure capture to finish.",
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        };
        if (waitForTelephonyEventName is not null) node["waitForTelephonyEventName"] = waitForTelephonyEventName;
        if (waitForTelephonyEventTimeoutSeconds is not null) node["waitForTelephonyEventTimeoutSeconds"] = waitForTelephonyEventTimeoutSeconds.Value;
        return node;
    }

    [Fact]
    public async Task WaitForTelephonyEventNameSet_PassesThroughToState()
    {
        var handler = new ScriptNodeHandler(new VariableResolver());

        var result = await handler.ExecuteAsync(
            Node(waitForTelephonyEventName: "cc_capture"), Ctx(), agentInput: null, agentTransition: "default");

        Assert.Equal("cc_capture", result.State.WaitForTelephonyEventName);
        Assert.Equal("n_next", result.NextNodeId);
    }

    [Fact]
    public async Task WaitForTelephonyEventNameAbsent_DefaultsToNull()
    {
        var handler = new ScriptNodeHandler(new VariableResolver());

        var result = await handler.ExecuteAsync(
            Node(), Ctx(), agentInput: null, agentTransition: "default");

        Assert.Null(result.State.WaitForTelephonyEventName);
    }

    [Fact]
    public async Task WaitForTelephonyEventNameEmpty_StaysEmpty()
    {
        var handler = new ScriptNodeHandler(new VariableResolver());

        var result = await handler.ExecuteAsync(
            Node(waitForTelephonyEventName: ""), Ctx(), agentInput: null, agentTransition: "default");

        Assert.Equal("", result.State.WaitForTelephonyEventName);
    }

    [Fact]
    public async Task WaitForTelephonyEventTimeoutSecondsSet_PassesThroughToState()
    {
        var handler = new ScriptNodeHandler(new VariableResolver());

        var result = await handler.ExecuteAsync(
            Node(waitForTelephonyEventName: "cc_capture", waitForTelephonyEventTimeoutSeconds: 120),
            Ctx(), agentInput: null, agentTransition: "default");

        Assert.Equal(120, result.State.WaitForTelephonyEventTimeoutSeconds);
    }

    [Fact]
    public async Task WaitForTelephonyEventTimeoutSecondsAbsent_DefaultsToNull()
    {
        var handler = new ScriptNodeHandler(new VariableResolver());

        var result = await handler.ExecuteAsync(
            Node(waitForTelephonyEventName: "cc_capture"), Ctx(), agentInput: null, agentTransition: "default");

        Assert.Null(result.State.WaitForTelephonyEventTimeoutSeconds);
    }

    [Fact]
    public async Task UnresolvedVariableInContent_ShowsNotCapturedPlaceholder_AgentFacingContentOnly()
    {
        // Script content is agent-facing display, so it goes through ResolveForDisplay, not the
        // plain Resolve every functional (non-display) consumer uses — see
        // ContactConnection.Infrastructure/FlowEngine/VariableResolver.cs.
        var handler = new ScriptNodeHandler(new VariableResolver());
        var node = new JsonObject
        {
            ["type"]        = "script",
            ["content"]     = "Hello {{flow.never_captured}}!",
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        };

        var result = await handler.ExecuteAsync(node, Ctx(), agentInput: null, agentTransition: "default");

        Assert.Equal("Hello [not captured]!", result.State.Content);
    }
}
