using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// OnAgentSelectedNodeHandler (tf_on_agent_selected — note: lives in the file
/// OnAgentSelectedNodeHandler.cs, renamed from the stale EventWaitNodeHandler.cs during the
/// telephony node review; the class itself was never named EventWaitNodeHandler),
/// OnAgentAnswerNodeHandler, OnCallDisconnectedNodeHandler, and OnCustomEventNodeHandler are event
/// branch entry points with identical bodies — pure pass-through to the "default" transition, no
/// I/O, no branching. Covered together in one file rather than four near-duplicate ones.
/// </summary>
public class EventListenerNodeHandlersTests
{
    private static TelephonyFlowContext Ctx() => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    public static IEnumerable<object[]> Handlers()
    {
        yield return [new OnAgentSelectedNodeHandler(), "tf_on_agent_selected"];
        yield return [new OnAgentAnswerNodeHandler(), "tf_on_agent_answer"];
        yield return [new OnCallDisconnectedNodeHandler(), "tf_on_call_disconnected"];
        yield return [new OnCustomEventNodeHandler(), "tf_on_custom_event"];
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task NodeType_MatchesExpectedString(ITelephonyNodeHandler handler, string expectedType)
    {
        Assert.Equal(expectedType, handler.NodeType);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task FollowsDefaultTransition(ITelephonyNodeHandler handler, string _)
    {
        var node = new JsonObject { ["transitions"] = new JsonObject { ["default"] = "node_next" } };
        var result = await handler.ExecuteAsync(node, Ctx());
        Assert.Equal("node_next", result.NextNodeId);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task NoDefaultTransition_NextNodeIdIsNull_NoThrow(ITelephonyNodeHandler handler, string _)
    {
        var node = new JsonObject();
        var result = await handler.ExecuteAsync(node, Ctx());
        Assert.Null(result.NextNodeId);
    }
}
