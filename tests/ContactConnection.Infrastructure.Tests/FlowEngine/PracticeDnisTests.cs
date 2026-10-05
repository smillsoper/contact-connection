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

/// <summary>
/// set_variable {{call_record.dnis}} (S179): a practice run can carry a test DNIS (Life Seasons ties products to the DNIS,
/// so a sandbox order must match), but a live call's DNIS is what the caller dialed and never changes.
/// </summary>
public class PracticeDnisTests
{
    private static async Task<(CallRecord Record, FlowExecutionContext Ctx)> Run(CallRecord record)
    {
        var repo = new Mock<ICallRecordRepository>();
        repo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var service = new CallAddressService(repo.Object, Mock.Of<ICartService>());
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), service);
        var ctx = new FlowExecutionContext
        {
            SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1, CallRecordId = record.Id,
            InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CurrentNodeId = "n_set",
        };
        var node = new JsonObject
        {
            ["type"] = "set_variable",
            ["assignments"] = new JsonArray(new JsonObject { ["variable"] = "{{call_record.dnis}}", ["value"] = "(855) 123-4567" }),
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        };
        await handler.ExecuteAsync(node, ctx, null, "");
        return (record, ctx);
    }

    [Theory]
    [InlineData(CallRunMode.Training)]
    [InlineData(CallRunMode.Sandbox)]
    public async Task PracticeRun_SetsTheRecordsDnis(string mode)
    {
        var (record, ctx) = await Run(CallRecord.CreateManual(Guid.NewGuid(), Guid.NewGuid(), mode));

        Assert.Equal("8551234567", record.Dnis);
        Assert.Equal("8551234567", new VariableResolver().Resolve("{{call_record.dnis}}", ctx.ToVariableContext()));
    }

    [Fact]
    public async Task LiveCall_DnisNeverChanges()
    {
        var live = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        live.SetDnis("8005550100");

        var (record, ctx) = await Run(live);

        Assert.Equal("8005550100", record.Dnis);
        Assert.False(ctx.CallRecord.ContainsKey("dnis"));
    }
}
