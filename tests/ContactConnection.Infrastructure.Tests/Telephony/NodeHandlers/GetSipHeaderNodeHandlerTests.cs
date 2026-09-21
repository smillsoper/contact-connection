using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class GetSipHeaderNodeHandlerTests
{
    private static readonly GetSipHeaderNodeHandler Handler = new();

    private static TelephonyFlowContext Ctx(IReadOnlyDictionary<string, string>? channelVars = null) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        ChannelVars = channelVars ?? new Dictionary<string, string>(),
    };

    private static JsonObject Node(string? headerName, string? variableName) => new()
    {
        ["type"] = "tf_get_sip_header",
        ["headerName"] = headerName,
        ["variableName"] = variableName,
        ["transitions"] = new JsonObject { ["default"] = "node_next" },
    };

    [Fact]
    public async Task HeaderPresent_StoresValueIntoNamedVariable()
    {
        var ctx = Ctx(new Dictionary<string, string> { ["X-Original-ANI"] = "5551239999" });

        var result = await Handler.ExecuteAsync(Node("X-Original-ANI", "custom_ani"), ctx);

        Assert.Equal("node_next", result.NextNodeId);
        Assert.Equal("5551239999", ctx.Vars["custom_ani"]);
    }

    [Fact]
    public async Task HeaderMissing_StoresEmptyString_NotMissingKey()
    {
        var ctx = Ctx();
        await Handler.ExecuteAsync(Node("X-Not-Present", "custom_ani"), ctx);
        Assert.Equal("", ctx.Vars["custom_ani"]);
    }

    [Fact]
    public async Task NoHeaderName_DoesNotWriteAnyVariable()
    {
        var ctx = Ctx(new Dictionary<string, string> { ["X-Original-ANI"] = "5551239999" });
        await Handler.ExecuteAsync(Node(null, "custom_ani"), ctx);
        Assert.False(ctx.Vars.ContainsKey("custom_ani"));
    }

    [Fact]
    public async Task NoVariableName_DoesNotWriteAnyVariable()
    {
        var ctx = Ctx(new Dictionary<string, string> { ["X-Original-ANI"] = "5551239999" });
        var before = ctx.Vars.Count;
        await Handler.ExecuteAsync(Node("X-Original-ANI", null), ctx);
        Assert.Equal(before, ctx.Vars.Count);
    }
}
