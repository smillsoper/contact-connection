using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

public class GetCustomFieldNodeHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = TenantId, CurrentNodeId = "n_get",
    };

    private static JsonObject Node(string? definitionId, string? outputVariable) => new()
    {
        ["type"] = "get_custom_field",
        ["definitionId"] = definitionId,
        ["outputVariable"] = outputVariable,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    private static CustomFieldDefinition Def(string dataType = "string") =>
        CustomFieldDefinition.Create(TenantId, "f", "F", dataType);

    [Fact]
    public async Task StringValue_StoresRawStringIntoFlowVar()
    {
        var ctx = Ctx();
        var def = Def();
        var value = CustomFieldValue.Create(ctx.CallRecordId, def.Id);
        value.SetString("east coast");
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(ctx.CallRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedCustomField(def, value)]);
        var handler = new GetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node(def.Id.ToString(), "region"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("east coast", ctx.FlowVars["region"]);
    }

    [Fact]
    public async Task BooleanValue_FormatsAsLowercaseTrueFalse()
    {
        var ctx = Ctx();
        var def = Def("boolean");
        var value = CustomFieldValue.Create(ctx.CallRecordId, def.Id);
        value.SetBoolean(false);
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(ctx.CallRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedCustomField(def, value)]);
        var handler = new GetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        await handler.ExecuteAsync(Node(def.Id.ToString(), "upsell"), ctx, agentInput: null, agentTransition: "");

        // Regression guard: `false` must not be treated as "no value" by any ?? chain.
        Assert.Equal("false", ctx.FlowVars["upsell"]);
    }

    [Fact]
    public async Task NoValueYetStored_ResolvesToEmptyString_NotAFailure()
    {
        var ctx = Ctx();
        var def = Def();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(ctx.CallRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedCustomField(def, null)]);
        var handler = new GetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node(def.Id.ToString(), "region"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("", ctx.FlowVars["region"]);
    }

    [Fact]
    public async Task DefinitionNotInResolvedList_ResolvesToEmptyString()
    {
        var ctx = Ctx();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(ctx.CallRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new GetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString(), "region"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("", ctx.FlowVars["region"]);
    }

    [Fact]
    public async Task NoOutputVariable_DoesNotCallService_NoFlowVarWritten()
    {
        var ctx = Ctx();
        var customFields = new Mock<ICustomFieldService>();
        var handler = new GetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString(), null), ctx, agentInput: null, agentTransition: "");

        customFields.Verify(s => s.GetFieldsForCallAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MalformedDefinitionGuid_DoesNotCallService()
    {
        var ctx = Ctx();
        var customFields = new Mock<ICustomFieldService>();
        var handler = new GetCustomFieldNodeHandler(new VariableResolver(), customFields.Object);

        var result = await handler.ExecuteAsync(Node("not-a-guid", "region"), ctx, agentInput: null, agentTransition: "");

        Assert.Equal("n_next", result.NextNodeId);
        customFields.Verify(s => s.GetFieldsForCallAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
