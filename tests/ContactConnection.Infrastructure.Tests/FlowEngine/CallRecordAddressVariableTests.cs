using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>
/// {{call_record.billing_address}} / {{call_record.shipping_address}}: settable from a set_variable
/// node (the "ship to the billing address? → yes" path, no second address node needed) and
/// readable with nested properties like any flow address object.
/// </summary>
public class CallRecordAddressVariableTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1,
        CallRecordId = Guid.NewGuid(), InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(), CurrentNodeId = "n_set",
    };

    private static readonly string BillingJson = new JsonObject
    {
        ["firstName"] = "John", ["lastName"] = "Doe", ["address1Prefix"] = "", ["address1"] = "123 Main St",
        ["address2Prefix"] = "Apt", ["address2"] = "4", ["city"] = "Denver", ["state"] = "co",
        ["zip"] = "80202", ["country"] = "US", ["isVerified"] = true, ["isPOBox"] = false,
    }.ToJsonString();

    private static JsonObject SetNode(string variable, string value) => new()
    {
        ["type"] = "set_variable",
        ["assignments"] = new JsonArray(new JsonObject { ["variable"] = variable, ["value"] = value }),
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task SetVariable_ShippingAddressFromBilling_SavesToCallRecord_AndUpdatesContext()
    {
        var ctx = Ctx();
        ctx.FlowVars["billing_address"] = BillingJson;
        var addresses = new Mock<ICallAddressService>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), addresses.Object);

        var result = await handler.ExecuteAsync(
            SetNode("{{call_record.shipping_address}}", "{{flow.billing_address}}"), ctx, null, "");

        Assert.Equal("n_next", result.NextNodeId);
        addresses.Verify(a => a.SetAsync(ctx.CallRecordId, CallAddressRole.Shipping,
            It.Is<AddressData>(d => d.Street == "123 Main St" && d.State == "CO" && d.Zip == "80202" && d.Unit == "4" && d.IsVerified),
            It.IsAny<CancellationToken>()), Times.Once);

        // Visible to later nodes in the same flow, with nested access.
        var resolver = new VariableResolver();
        Assert.Equal("Denver", resolver.Resolve("{{call_record.shipping_address.city}}", ctx.ToVariableContext()));
        Assert.False(ctx.FlowVars.ContainsKey("call_record.shipping_address"));
    }

    [Fact]
    public async Task SetVariable_BillingTarget_UsesBillingRole()
    {
        var ctx = Ctx();
        ctx.FlowVars["addr"] = BillingJson;
        var addresses = new Mock<ICallAddressService>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), addresses.Object);

        await handler.ExecuteAsync(SetNode("call_record.billing_address", "{{flow.addr}}"), ctx, null, "");

        addresses.Verify(a => a.SetAsync(ctx.CallRecordId, CallAddressRole.Billing, It.IsAny<AddressData>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetVariable_NonAddressValue_IsIgnored_NeverWipesTheCallsAddress()
    {
        var ctx = Ctx(); // flow.billing_address never set → resolves to ""
        var addresses = new Mock<ICallAddressService>(MockBehavior.Strict);
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), addresses.Object);

        var result = await handler.ExecuteAsync(
            SetNode("{{call_record.shipping_address}}", "{{flow.billing_address}}"), ctx, null, "");

        Assert.Equal("n_next", result.NextNodeId); // strict mock proves no save
    }

    [Fact]
    public void CallAddressJson_RoundTrips()
    {
        var data = CallAddressJson.ToAddressData(BillingJson)!;
        var back = CallAddressJson.ToJsonObject(data);

        Assert.Equal("123 Main St", back["address1"]!.GetValue<string>());
        Assert.Equal("Apt 4", back["formattedAddress2"]!.GetValue<string>());
        Assert.Equal("CO", back["state"]!.GetValue<string>());
        Assert.Equal("123 Main St, Apt 4, Denver, CO 80202", back["fullAddress"]!.GetValue<string>());
        Assert.True(back["isVerified"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("\"a string\"")]
    [InlineData("{\"foo\":1}")]
    public void CallAddressJson_NonAddress_IsNull(string value)
        => Assert.Null(CallAddressJson.ToAddressData(value));

    [Fact]
    public void Resolver_CallRecordFlatKeysStillWork()
    {
        var ctx = Ctx();
        ctx.CallRecord["order_number"] = "LIFSEA-10000001";
        Assert.Equal("LIFSEA-10000001", new VariableResolver().Resolve("{{call_record.order_number}}", ctx.ToVariableContext()));
    }
}
