using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

public class SetCustomFieldNodeHandlerTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_set",
    };

    private static JsonObject Node(string? definitionId, string value) => new()
    {
        ["type"] = "set_custom_field",
        ["definitionId"] = definitionId,
        ["value"] = value,
        ["transitions"] = new JsonObject
        {
            ["success"] = "n_success", ["invalid_value"] = "n_invalid", ["error"] = "n_error",
        },
    };

    [Fact]
    public async Task SuccessfulSet_TakesSuccessTransition_ResolvesValueTemplate()
    {
        var ctx = Ctx();
        ctx.FlowVars["entered_phone"] = "5416704541";
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        var handler = new SetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(
            Node(definitionId.ToString(), "{{flow.entered_phone}}"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_success", result.NextNodeId);
        customFields.Verify(s => s.SetValueAsync(ctx.CallRecordId, definitionId, "5416704541", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FormatException_TakesInvalidValueTransition()
    {
        var ctx = Ctx();
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.SetValueAsync(ctx.CallRecordId, definitionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("not a number"));
        var handler = new SetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node(definitionId.ToString(), "not-a-number"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_invalid", result.NextNodeId);
    }

    [Fact]
    public async Task ArgumentException_TakesInvalidValueTransition()
    {
        var ctx = Ctx();
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.SetValueAsync(ctx.CallRecordId, definitionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("unsupported type"));
        var handler = new SetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node(definitionId.ToString(), "x"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_invalid", result.NextNodeId);
    }

    [Fact]
    public async Task InvalidOperationException_TakesErrorTransition()
    {
        var ctx = Ctx();
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.SetValueAsync(ctx.CallRecordId, definitionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("definition not found"));
        var handler = new SetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node(definitionId.ToString(), "x"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_error", result.NextNodeId);
    }

    [Fact]
    public async Task NoDefinitionId_TakesErrorTransition_NoServiceCall()
    {
        var ctx = Ctx();
        var customFields = new Mock<ICustomFieldService>();
        var handler = new SetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node(null, "x"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_error", result.NextNodeId);
        customFields.Verify(s => s.SetValueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MalformedDefinitionGuid_TakesErrorTransition()
    {
        var ctx = Ctx();
        var customFields = new Mock<ICustomFieldService>();
        var handler = new SetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node("not-a-guid", "x"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_error", result.NextNodeId);
    }
}
