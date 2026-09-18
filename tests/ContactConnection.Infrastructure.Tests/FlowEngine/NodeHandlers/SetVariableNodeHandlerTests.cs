using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>
/// CRM "set_variable" node. Focused on the shared.* dispatch branch added alongside
/// ISharedCallVariableStore — a "shared.key" assignment target writes to the call-wide store
/// (visible to the telephony call flow for the same call as {{shared.*}} there too) instead of
/// this CRM session's own FlowVars, which stays private to this session. Existing flow./caller./
/// agent./tenant. dispatch is unchanged and covered here only enough to prove the new branch
/// didn't regress them (this handler had no prior test coverage).
/// </summary>
public class SetVariableNodeHandlerTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_set",
    };

    private static JsonObject Node(params (string variable, string value)[] assignments)
    {
        var arr = new JsonArray();
        foreach (var (variable, value) in assignments)
            arr.Add(new JsonObject { ["variable"] = variable, ["value"] = value });

        return new JsonObject
        {
            ["type"]        = "set_variable",
            ["assignments"] = arr,
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        };
    }

    [Fact]
    public async Task SharedAssignment_WritesToStore_AndToCtxSharedVarsImmediately()
    {
        var ctx = Ctx();
        var store = new Mock<ISharedCallVariableStore>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), store.Object);

        var result = await handler.ExecuteAsync(
            Node(("shared.CC_Capture_Success", "true")), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("true", ctx.SharedVars["CC_Capture_Success"]);
        store.Verify(s => s.SetAsync(ctx.CallRecordId, "CC_Capture_Success", "true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SharedAssignment_DoesNotAlsoWriteToFlowVars()
    {
        var ctx = Ctx();
        var store = new Mock<ISharedCallVariableStore>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), store.Object);

        await handler.ExecuteAsync(
            Node(("shared.CC_Capture_Success", "true")), ctx, agentInput: null, agentTransition: "");

        Assert.False(ctx.FlowVars.ContainsKey("CC_Capture_Success"));
        Assert.False(ctx.FlowVars.ContainsKey("shared.CC_Capture_Success"));
    }

    [Fact]
    public async Task PlainAssignment_StillWritesToFlowVars_NotTheSharedStore()
    {
        var ctx = Ctx();
        var store = new Mock<ISharedCallVariableStore>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), store.Object);

        await handler.ExecuteAsync(
            Node(("orderTotal", "19.99")), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("19.99", ctx.FlowVars["orderTotal"]);
        store.Verify(s => s.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValueTemplateCanReferenceAnExistingSharedVar()
    {
        var ctx = Ctx();
        ctx.SharedVars["CC_Capture_Success"] = "true";
        var store = new Mock<ISharedCallVariableStore>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), store.Object);

        await handler.ExecuteAsync(
            Node(("disposition", "{{shared.CC_Capture_Success}}")), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("true", ctx.FlowVars["disposition"]);
    }
}
