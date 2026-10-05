using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// Sprint 1 item 3 (S179): telephony nodes reached with no ESL connection (a trigger_telephony_event branch) log and move
/// on instead of throwing a NullReferenceException that would kill the flow.
/// </summary>
public class NoEslGuardTests
{
    private static TelephonyFlowContext Ctx() => new()
    {
        ChannelUuid = "u", CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = null,
    };

    private static JsonObject Node(params (string Key, string Value)[] fields)
    {
        var node = new JsonObject { ["transitions"] = new JsonObject { ["default"] = "next" } };
        foreach (var (k, v) in fields) node[k] = v;
        return node;
    }

    [Fact]
    public async Task Answer_FollowsDefault_WithoutMarkingAnswered()
    {
        var ctx = Ctx();
        var result = await new AnswerNodeHandler().ExecuteAsync(Node(), ctx);
        Assert.Equal("next", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_answered"));
    }

    [Fact]
    public async Task Hangup_AndReject_EndTheFlow()
    {
        Assert.Null((await new HangupNodeHandler().ExecuteAsync(Node(), Ctx())).NextNodeId);
        var rejected = await new RejectNodeHandler().ExecuteAsync(Node(("cause", "busy")), Ctx());
        Assert.Equal((null, "rejected"), (rejected.NextNodeId, rejected.TransitionTaken));
    }

    [Fact]
    public async Task SetCallerId_AndSipHeader_FollowDefault()
    {
        Assert.Equal("next", (await new SetCallerIdNodeHandler().ExecuteAsync(Node(("callerIdValue", "+15035551234")), Ctx())).NextNodeId);
        Assert.Equal("next", (await new SetSipHeaderNodeHandler().ExecuteAsync(Node(("headerName", "X-Test"), ("value", "1")), Ctx())).NextNodeId);
    }

    [Fact]
    public async Task RouteToQueue_DirectExtension_FollowsDefault()
    {
        var handler = new RouteToQueueNodeHandler(null!, null!, null!, null!);   // direct-extension path touches none of them
        var result = await handler.ExecuteAsync(Node(("agentExtension", "1001")), Ctx());
        Assert.Equal("next", result.NextNodeId);
    }
}
