using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.CustomFields;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>Set Disposition (S181). The catalog-choice path reads the disposition's current name from the tenant DB and is
/// covered live; these cover the variable path and how the value is written.</summary>
public class SetDispositionNodeHandlerTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_disp",
    };

    private static JsonObject Node(string value) => new()
    {
        ["type"] = "set_disposition",
        ["dispositionId"] = "",
        ["value"] = value,
        ["transitions"] = new JsonObject { ["success"] = "n_ok", ["error"] = "n_err" },
    };

    private static Mock<ICustomFieldService> FieldsWithDisposition(out CustomFieldDefinition def)
    {
        def = CustomFieldDefinition.Create(Guid.NewGuid(), "disposition", "Disposition", CustomFieldDataType.String);
        var d = def;
        var mock = new Mock<ICustomFieldService>();
        mock.Setup(s => s.GetFieldsForCallAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedCustomField(d, null)]);
        return mock;
    }

    [Fact]
    public async Task FromAVariable_WritesTheDispositionField_AndFlowDisposition()
    {
        var ctx = Ctx();
        ctx.Inputs["node_003"] = "Retention save";
        var fields = FieldsWithDisposition(out var def);
        var handler = new SetDispositionNodeHandler(new VariableResolver(), fields.Object, null!);

        var result = await handler.ExecuteAsync(Node("{{input.node_003}}"), ctx, null, "");

        Assert.Equal("n_ok", result.NextNodeId);
        Assert.Equal("Retention save", ctx.FlowVars["disposition"]);
        fields.Verify(s => s.SetValueFromScriptAsync(ctx.CallRecordId, ctx.InteractionId, def.Id, "Retention save", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoDispositionFieldInScope_StillSucceeds_ThroughFlowDisposition()
    {
        var ctx = Ctx();
        var fields = FieldsWithDisposition(out var def);
        fields.Setup(s => s.SetValueFromScriptAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), def.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not in scope for interaction"));
        var handler = new SetDispositionNodeHandler(new VariableResolver(), fields.Object, null!);

        var result = await handler.ExecuteAsync(Node("Cancelled Subscription"), ctx, null, "");

        Assert.Equal("n_ok", result.NextNodeId);
        Assert.Equal("Cancelled Subscription", ctx.FlowVars["disposition"]);
    }

    [Fact]
    public async Task NothingToRecord_TakesTheErrorExit()
    {
        var ctx = Ctx();
        var fields = FieldsWithDisposition(out _);
        var handler = new SetDispositionNodeHandler(new VariableResolver(), fields.Object, null!);

        var result = await handler.ExecuteAsync(Node("{{input.never_answered}}"), ctx, null, "");

        Assert.Equal("n_err", result.NextNodeId);
        fields.Verify(s => s.SetValueFromScriptAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void RenamingADisposition_KeepsTheOldNameAsAnAlias()
    {
        var d = Disposition.Create(Guid.NewGuid(), "Referred to Customer Service", null, Guid.NewGuid(), null, null);
        d.Update("Referred to CS", null, d.CategoryId, [], 0);
        Assert.True(d.Matches("Referred to Customer Service"));   // past calls stay linked
        Assert.True(d.Matches("referred to cs"));
        d.Update("Referred to CS", null, d.CategoryId, d.Aliases, 0);   // same name again: no duplicate alias
        Assert.Single(d.Aliases);
    }
}
