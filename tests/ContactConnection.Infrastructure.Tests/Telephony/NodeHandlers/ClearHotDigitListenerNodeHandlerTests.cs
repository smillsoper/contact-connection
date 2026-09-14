using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>tf_clear_hot_digit — "Clear DTMF Listener" in the designer. Explicitly disarms a
/// tf_ivr_menu(alwaysListen=true) hot-digit listener.</summary>
public class ClearHotDigitListenerNodeHandlerTests
{
    private static TelephonyFlowContext Ctx(params (string k, string v)[] vars)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = "call-uuid-clear", CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        };
        foreach (var (k, v) in vars) ctx.Vars[k] = v;
        return ctx;
    }

    private static JsonObject Node() => new()
    {
        ["type"] = "tf_clear_hot_digit",
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task ArmedListener_IsClearedFromContextAndMarkedForSessionRemoval()
    {
        var handler = new ClearHotDigitListenerNodeHandler(NullLogger<ClearHotDigitListenerNodeHandler>.Instance);
        var ctx = Ctx(("_hot_digit_options", "{\"1\":\"node_x\"}"), ("_hot_digit_node_id", "tf_ivr_menu_1"));

        var result = await handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_hot_digit_options"));
        Assert.False(ctx.Vars.ContainsKey("_hot_digit_node_id"));
        Assert.Contains("_hot_digit_options", ctx.VarsToRemove);
        Assert.Contains("_hot_digit_node_id", ctx.VarsToRemove);
    }

    [Fact]
    public async Task NothingArmed_IsANoOp_StillFollowsDefault()
    {
        var handler = new ClearHotDigitListenerNodeHandler(NullLogger<ClearHotDigitListenerNodeHandler>.Instance);
        var ctx = Ctx();

        var result = await handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Empty(ctx.VarsToRemove);
    }
}
