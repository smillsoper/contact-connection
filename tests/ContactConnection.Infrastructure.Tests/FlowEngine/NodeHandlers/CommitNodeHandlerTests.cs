using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;
using CrmFlowEngine = ContactConnection.Infrastructure.FlowEngine.FlowEngine;

namespace ContactConnection.Infrastructure.Tests.FlowEngine.NodeHandlers;

/// <summary>Commit Point (S164): a designer-placed point of no return. Passing it records a
/// commitment event on the call record and puts the session in committed mode, where the engine
/// refuses section jumps except to the allowed sections — and that state survives the session being
/// saved to/loaded from Redis between agent actions.</summary>
public class CommitNodeHandlerTests
{
    private static FlowExecutionContext Ctx(Guid callRecordId) => new()
    {
        SessionId = Guid.NewGuid(), CallRecordId = callRecordId, TenantId = Guid.NewGuid(), CurrentNodeId = "commit_1",
    };

    private static JsonObject Node(params string[] allowed) => new()
    {
        ["type"] = "commit",
        ["label"] = "Order submitted",
        ["eventName"] = "order_submitted",
        ["lockLabel"] = "Order submitted — finalize this call as an order",
        ["allowedSectionIds"] = new JsonArray(allowed.Select(a => (JsonNode)a).ToArray()),
        ["transitions"] = new JsonObject { ["default"] = "next" },
    };

    private static (CommitNodeHandler Handler, CallRecord Record, Mock<ICallRecordRepository> Repo) Setup()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repo = new Mock<ICallRecordRepository>();
        repo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        return (new CommitNodeHandler(new VariableResolver(), repo.Object), record, repo);
    }

    [Fact]
    public async Task Commit_RecordsEvent_SetsCommittedState_AndAdvances()
    {
        var (handler, record, repo) = Setup();
        var ctx = Ctx(record.Id);

        var result = await handler.ExecuteAsync(Node("sec_closing"), ctx, null, "default");

        Assert.Equal("next", result.NextNodeId);
        Assert.True(ctx.IsCommitted);
        Assert.Equal("Order submitted — finalize this call as an order", ctx.CommitLabel);
        Assert.Equal(["sec_closing"], ctx.CommitAllowedSections);
        var evt = Assert.Single(record.CommitmentEvents);
        Assert.Equal("order_submitted", evt.EventName);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SecondCommit_OnlyNarrows_AndDoesNotDuplicateTheEvent()
    {
        var (handler, record, _) = Setup();
        var ctx = Ctx(record.Id);

        await handler.ExecuteAsync(Node("sec_a", "sec_b"), ctx, null, "default");
        await handler.ExecuteAsync(Node("sec_b", "sec_c"), ctx, null, "default");

        Assert.Equal(["sec_b"], ctx.CommitAllowedSections);   // sec_c not reopened
        Assert.Single(record.CommitmentEvents);
    }

    [Fact]
    public void JumpGuard_BlocksEarlierSections_AllowsListedOnes_AndIsInertBeforeCommit()
    {
        var ctx = Ctx(Guid.NewGuid());
        CrmFlowEngine.EnsureJumpAllowed(ctx, "sec_opening");   // not committed yet: anything goes

        ctx.CommitEventName = "order_submitted";
        ctx.CommitLabel = "Order submitted";
        ctx.CommitAllowedSections = ["sec_closing"];

        var ex = Assert.Throws<InvalidOperationException>(() => CrmFlowEngine.EnsureJumpAllowed(ctx, "sec_opening"));
        Assert.Equal("Order submitted", ex.Message);
        CrmFlowEngine.EnsureJumpAllowed(ctx, "sec_closing");
    }

    [Fact]
    public void CommittedState_SurvivesSessionSaveAndLoad()
    {
        var ctx = Ctx(Guid.NewGuid());
        ctx.CommitEventName = "order_submitted";
        ctx.CommitLabel = "Order submitted";
        ctx.CommitAllowedSections = ["sec_closing"];

        var loaded = FlowExecutionContext.Deserialize(ctx.SessionId, Guid.NewGuid(), 1, ctx.CallRecordId, Guid.NewGuid(),
            Guid.NewGuid(), ctx.TenantId, "n", "{}", ctx.SerializeVariableStore(), "[]", [], [], [], []);

        Assert.True(loaded.IsCommitted);
        Assert.Equal("Order submitted", loaded.CommitLabel);
        Assert.Equal(["sec_closing"], loaded.CommitAllowedSections);
    }

    [Fact]
    public void SessionsStoredBeforeCommitPoints_StillLoad_Uncommitted()
    {
        var legacy = """{"FlowVars":{},"Inputs":{},"ApiResults":{},"CurrentSectionNodeId":null,"CurrentSectionName":null,"CurrentSectionLocked":false,"CompletedSectionNodeIds":[],"EncounteredSectionNodeIds":[],"CallStack":[]}""";
        var loaded = FlowExecutionContext.Deserialize(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "n", "{}", legacy, "[]", [], [], [], []);

        Assert.False(loaded.IsCommitted);
        Assert.Empty(loaded.CommitAllowedSections);
    }
}
