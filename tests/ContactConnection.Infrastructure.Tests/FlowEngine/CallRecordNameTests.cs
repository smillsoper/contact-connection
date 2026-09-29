using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>The customer name on the call record (S165): set_variable {{call_record.first_name}} /
/// {{caller.last_name}} persist it, so the call detail view and order templates see it.</summary>
public class CallRecordNameTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1, CallRecordId = Guid.NewGuid(),
        InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CurrentNodeId = "n_set",
    };

    private static JsonObject SetNode(params (string Variable, string Value)[] assignments) => new()
    {
        ["type"] = "set_variable",
        ["assignments"] = new JsonArray(assignments
            .Select(a => (JsonNode)new JsonObject { ["variable"] = a.Variable, ["value"] = a.Value }).ToArray()),
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task SetVariable_CustomerName_SavesToCallRecord_AndUpdatesCallerVars()
    {
        var ctx = Ctx();
        ctx.Inputs["v1_first_name"] = " Bob ";
        ctx.FlowVars["billing_address"] = """{"firstName":"Robert","lastName":"Test"}""";
        var contact = new Mock<ICallAddressService>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), contact.Object);

        await handler.ExecuteAsync(SetNode(
            ("{{call_record.first_name}}", "{{input.v1_first_name}}"),
            ("caller.last_name", "{{flow.billing_address.lastName}}"),
            ("call_record.last_name", "{{flow.never_set}}")), ctx, null, "");

        contact.Verify(c => c.SetNameAsync(ctx.CallRecordId, "Bob", null, It.IsAny<CancellationToken>()), Times.Once);
        contact.Verify(c => c.SetNameAsync(ctx.CallRecordId, null, "Test", It.IsAny<CancellationToken>()), Times.Once);
        contact.VerifyNoOtherCalls(); // the blank value never wipes the saved last name
        var resolver = new VariableResolver();
        Assert.Equal("Bob Test", resolver.Resolve("{{caller.name}}", ctx.ToVariableContext()));
        Assert.Equal("Bob", resolver.Resolve("{{call_record.first_name}}", ctx.ToVariableContext()));
        Assert.Equal("Test", resolver.Resolve("{{caller.last_name}}", ctx.ToVariableContext()));
    }

    [Fact]
    public async Task SetNameAsync_UpdatesOnlyTheGivenPart()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        record.SetCallerIdentity("Old", "Name", "a@b.com", "5415550000", "ACC1");
        var repo = new Mock<ICallRecordRepository>();
        repo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var service = new CallAddressService(repo.Object, Mock.Of<ICartService>());

        await service.SetNameAsync(record.Id, "Bob", null);
        await service.SetNameAsync(record.Id, "  ", "  ");

        Assert.Equal(("Bob", "Name", "a@b.com", "5415550000", "ACC1"),
            (record.FirstName, record.LastName, record.Email, record.Phone, record.AccountNumber));
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
