using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

public class StoreValueNodeHandlerTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_store",
    };

    private static JsonObject Node(string scope, string keyName, string value, string? retention = null) => new()
    {
        ["type"] = "store_value",
        ["scope"] = scope,
        ["keyName"] = keyName,
        ["value"] = value,
        ["retention"] = retention,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task ResolvesKeyAndValueTemplates_BeforeCallingService()
    {
        var ctx = Ctx();
        ctx.FlowVars["order_id"] = "12345";
        ctx.Caller["phone"] = "+15551234567";
        var storedValues = new Mock<IStoredValueService>();
        var handler = new StoreValueNodeHandler(new VariableResolver(), storedValues.Object);

        var result = await handler.ExecuteAsync(
            Node("campaign", "{{caller.phone}}_orderId", "{{flow.order_id}}"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        storedValues.Verify(s => s.SetAsync(
            ctx.CallRecordId, "campaign", "+15551234567_orderId", "12345", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("forever")]
    public async Task NoOrForeverRetention_PassesNullExpiresAt(string? retention)
    {
        var ctx = Ctx();
        var storedValues = new Mock<IStoredValueService>();
        var handler = new StoreValueNodeHandler(new VariableResolver(), storedValues.Object);

        await handler.ExecuteAsync(Node("tenant", "key", "value", retention), ctx, agentInput: null, agentTransition: "");

        storedValues.Verify(s => s.SetAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithRetention_PassesAFutureExpiresAt()
    {
        var ctx = Ctx();
        var storedValues = new Mock<IStoredValueService>();
        var handler = new StoreValueNodeHandler(new VariableResolver(), storedValues.Object);

        await handler.ExecuteAsync(Node("tenant", "key", "value", "1_hour"), ctx, agentInput: null, agentTransition: "");

        storedValues.Verify(s => s.SetAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<DateTimeOffset?>(d => d.HasValue && d.Value > DateTimeOffset.UtcNow),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BlankKey_SkipsServiceCall_StillFollowsDefault()
    {
        // A literal blank/whitespace-only key — the guard this test targets protects against the
        // flow author leaving the Key field empty in the designer. Note: an *unresolved* {{...}}
        // tag is NOT a way to produce an empty key here — VariableResolver.Resolve falls back to
        // the literal "[not captured]" for an unresolved tag (unlike the telephony engine's
        // resolver, which returns ""), so it would resolve to a non-empty (if odd) key instead.
        var ctx = Ctx();
        var storedValues = new Mock<IStoredValueService>();
        var handler = new StoreValueNodeHandler(new VariableResolver(), storedValues.Object);

        var result = await handler.ExecuteAsync(
            Node("tenant", "   ", "value"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        storedValues.Verify(s => s.SetAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DefaultsScopeToTenant_WhenOmitted()
    {
        var ctx = Ctx();
        var node = Node("tenant", "key", "value");
        node.Remove("scope");
        var storedValues = new Mock<IStoredValueService>();
        var handler = new StoreValueNodeHandler(new VariableResolver(), storedValues.Object);

        await handler.ExecuteAsync(node, ctx, agentInput: null, agentTransition: "");

        storedValues.Verify(s => s.SetAsync(
            It.IsAny<Guid>(), "tenant", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
