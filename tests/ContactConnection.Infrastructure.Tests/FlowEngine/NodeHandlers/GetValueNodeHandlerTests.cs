using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

public class GetValueNodeHandlerTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_get",
    };

    private static JsonObject Node(string scope, string keyName, string? outputVariable) => new()
    {
        ["type"] = "get_value",
        ["scope"] = scope,
        ["keyName"] = keyName,
        ["outputVariable"] = outputVariable,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task ResolvesKeyTemplate_ReturnsValueIntoFlowVar()
    {
        var ctx = Ctx();
        ctx.Caller["phone"] = "+15551234567";
        var storedValues = new Mock<IStoredValueService>();
        storedValues.Setup(s => s.GetAsync(ctx.CallRecordId, "campaign", "+15551234567_orderId", It.IsAny<CancellationToken>()))
            .ReturnsAsync("12345");
        var handler = new GetValueNodeHandler(new VariableResolver(), storedValues.Object);

        var result = await handler.ExecuteAsync(
            Node("campaign", "{{caller.phone}}_orderId", "lastOrderId"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("12345", ctx.FlowVars["lastOrderId"]);
    }

    [Fact]
    public async Task NoValueStored_ResolvesToEmptyString_NotAFailure()
    {
        var ctx = Ctx();
        var storedValues = new Mock<IStoredValueService>();
        storedValues.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var handler = new GetValueNodeHandler(new VariableResolver(), storedValues.Object);

        var result = await handler.ExecuteAsync(Node("tenant", "key", "myVar"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("", ctx.FlowVars["myVar"]);
    }

    [Fact]
    public async Task NoOutputVariable_DoesNotCallService()
    {
        var ctx = Ctx();
        var storedValues = new Mock<IStoredValueService>();
        var handler = new GetValueNodeHandler(new VariableResolver(), storedValues.Object);

        await handler.ExecuteAsync(Node("tenant", "key", null), ctx, agentInput: null, agentTransition: "");

        storedValues.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BlankKey_DoesNotCallService_StillWritesEmptyString()
    {
        // See StoreValueNodeHandlerTests.BlankKey_SkipsServiceCall_StillFollowsDefault for why an
        // unresolved {{...}} tag isn't the right way to produce an empty key on the CRM side.
        var ctx = Ctx();
        var storedValues = new Mock<IStoredValueService>();
        var handler = new GetValueNodeHandler(new VariableResolver(), storedValues.Object);

        await handler.ExecuteAsync(Node("tenant", "   ", "myVar"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("", ctx.FlowVars["myVar"]);
        storedValues.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
