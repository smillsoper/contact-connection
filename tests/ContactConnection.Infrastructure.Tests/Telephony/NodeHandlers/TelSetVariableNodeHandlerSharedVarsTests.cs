using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_set_variable's shared.* dispatch — writes to the call-wide ISharedCallVariableStore instead
/// of this telephony call's own ctx.Vars, and {{shared.*}} reads back out of ctx.SharedVars (which
/// TelephonyFlowEngine populates fresh from the store before dispatching to any node — not
/// exercised here, no automated coverage on TelephonyFlowEngine itself). Existing plain-key/
/// flow./caller.ani/call.did behavior is covered only enough to prove the new branch didn't
/// regress it (this handler had no prior test coverage).
/// </summary>
public class TelSetVariableNodeHandlerSharedVarsTests
{
    private static TelephonyFlowContext Ctx() => new()
    {
        ChannelUuid       = "uuid-1",
        CallerNumber      = "+15551234567",
        DestinationNumber = "+15557654321",
        TenantId          = Guid.NewGuid(),
        CampaignId        = Guid.NewGuid(),
        CallRecordId      = Guid.NewGuid(),
        TenantSubdomain   = "test-tenant",
        TenantSchemaName  = "tenant_test_tenant",
        TenantTimezone    = "America/Chicago",
    };

    private static JsonObject Node(params (string key, string value)[] assignments)
    {
        var arr = new JsonArray();
        foreach (var (key, value) in assignments)
            arr.Add(new JsonObject { ["key"] = key, ["value"] = value });

        return new JsonObject
        {
            ["type"]        = "tf_set_variable",
            ["assignments"] = arr,
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        };
    }

    [Fact]
    public async Task SharedKey_WritesToStore_AndToCtxSharedVarsImmediately()
    {
        var ctx = Ctx();
        var store = new Mock<ISharedCallVariableStore>();
        var handler = new TelSetVariableNodeHandler(store.Object);

        var result = await handler.ExecuteAsync(Node(("shared.CC_Capture_Success", "true")), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("true", ctx.SharedVars["CC_Capture_Success"]);
        store.Verify(s => s.SetAsync(ctx.CallRecordId, "CC_Capture_Success", "true", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SharedKey_DoesNotAlsoWriteToCtxVars()
    {
        var ctx = Ctx();
        var handler = new TelSetVariableNodeHandler(Mock.Of<ISharedCallVariableStore>());

        await handler.ExecuteAsync(Node(("shared.CC_Capture_Success", "true")), ctx);

        Assert.False(ctx.Vars.ContainsKey("CC_Capture_Success"));
        Assert.False(ctx.Vars.ContainsKey("shared.CC_Capture_Success"));
    }

    [Fact]
    public async Task PlainKey_StillWritesToCtxVars_NotTheSharedStore()
    {
        var ctx = Ctx();
        var store = new Mock<ISharedCallVariableStore>();
        var handler = new TelSetVariableNodeHandler(store.Object);

        await handler.ExecuteAsync(Node(("myVar", "hello")), ctx);

        Assert.Equal("hello", ctx.Vars["myVar"]);
        store.Verify(s => s.SetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValueTemplate_CanReadAnExistingSharedVar()
    {
        var ctx = Ctx();
        ctx.SharedVars["disposition"] = "sale";
        var handler = new TelSetVariableNodeHandler(Mock.Of<ISharedCallVariableStore>());

        await handler.ExecuteAsync(Node(("localCopy", "{{shared.disposition}}")), ctx);

        Assert.Equal("sale", ctx.Vars["localCopy"]);
    }

    [Fact]
    public void Resolve_StillHandlesWellKnownNamespaces_CallerAniAndCallDid()
    {
        var ctx = Ctx();

        Assert.Equal(ctx.CallerNumber, TelSetVariableNodeHandler.Resolve("{{caller.ani}}", ctx));
        Assert.Equal(ctx.DestinationNumber, TelSetVariableNodeHandler.Resolve("{{call.did}}", ctx));
    }
}
