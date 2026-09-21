using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class CancelDialNodeHandlerTests
{
    private static readonly CancelDialNodeHandler Handler = new();

    private static TelephonyFlowContext Ctx() => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    [Fact]
    public async Task SetsCancelledState_WithResolvedMessage_TerminalNoNextNode()
    {
        var ctx = Ctx();
        var node = new JsonObject { ["type"] = "tf_cancel_dial", ["cancelMessage"] = "Sorry {{caller.ani}}, closed now." };

        var result = await Handler.ExecuteAsync(node, ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("cancelled", result.TransitionTaken);
        Assert.True(ctx.IsCancelled);
        Assert.Equal("Sorry +15551234567, closed now.", ctx.CancelMessage);
    }

    [Fact]
    public async Task NoCancelMessage_StillCancels_WithEmptyMessage()
    {
        var ctx = Ctx();
        var node = new JsonObject { ["type"] = "tf_cancel_dial" };

        await Handler.ExecuteAsync(node, ctx);

        Assert.True(ctx.IsCancelled);
        Assert.Equal("", ctx.CancelMessage);
    }
}
