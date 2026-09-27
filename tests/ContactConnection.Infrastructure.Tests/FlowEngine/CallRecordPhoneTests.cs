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

/// <summary>Billing/shipping contact phones on the call record: phone node "save to call record
/// as", set_variable {{call_record.billing_phone}} / {{call_record.shipping_phone}}.</summary>
public class CallRecordPhoneTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1, CallRecordId = Guid.NewGuid(),
        InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CurrentNodeId = "n_phone",
    };

    private static JsonObject PhoneNode(string? role, bool required = true) => new()
    {
        ["type"] = "phone", ["label"] = "Billing Phone", ["outputVariable"] = "billing_phone",
        ["required"] = required, ["phoneRole"] = role,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    // ── Service ──────────────────────────────────────────────────────────────

    private static (CallAddressService Service, CallRecord Record) Service()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repo = new Mock<ICallRecordRepository>();
        repo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        return (new CallAddressService(repo.Object, Mock.Of<ICartService>()), record);
    }

    [Fact]
    public async Task SetPhone_Roles()
    {
        var (service, record) = Service();
        await service.SetPhoneAsync(record.Id, CallAddressRole.Billing, "5415551234");
        Assert.Equal("5415551234", record.BillingPhone);
        Assert.Null(record.ShippingPhone);

        await service.SetPhoneAsync(record.Id, CallAddressRole.Shipping, "3035550199");
        Assert.Equal("5415551234", record.BillingPhone);   // untouched
        Assert.Equal("3035550199", record.ShippingPhone);

        await service.SetPhoneAsync(record.Id, CallAddressRole.BillingAndShipping, "8005550100");
        Assert.Equal("8005550100", record.BillingPhone);
        Assert.Equal("8005550100", record.ShippingPhone);

        await service.SetPhoneAsync(record.Id, CallAddressRole.Billing, "  ");   // blank → no-op
        Assert.Equal("8005550100", record.BillingPhone);
    }

    // ── Phone node ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PhoneNode_WithRole_SavesDigits_AndUpdatesContext()
    {
        var addresses = new Mock<ICallAddressService>();
        var ctx = Ctx();
        var result = await new PhoneNodeHandler(new VariableResolver(), addresses.Object)
            .ExecuteAsync(PhoneNode("billing"), ctx, "(541) 555-1234", "");

        Assert.Equal("n_next", result.NextNodeId);
        addresses.Verify(a => a.SetPhoneAsync(ctx.CallRecordId, "billing", "5415551234", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("5415551234", new VariableResolver().Resolve("{{call_record.billing_phone}}", ctx.ToVariableContext()));
    }

    [Fact]
    public async Task PhoneNode_InvalidOrBlankOrNoRole_NeverSaves()
    {
        var addresses = new Mock<ICallAddressService>(MockBehavior.Strict);
        var handler = new PhoneNodeHandler(new VariableResolver(), addresses.Object);

        var invalid = await handler.ExecuteAsync(PhoneNode("billing"), Ctx(), "(541) 555", "");   // too short
        Assert.Null(invalid.NextNodeId);
        await handler.ExecuteAsync(PhoneNode("shipping", required: false), Ctx(), "", "");     // optional blank
        await handler.ExecuteAsync(PhoneNode(null), Ctx(), "(541) 555-1234", "");              // no role
        await handler.ExecuteAsync(PhoneNode("billing"), Ctx(), null, "");                     // first display
    }

    // ── set_variable ─────────────────────────────────────────────────────────

    private static JsonObject SetNode(string variable, string value) => new()
    {
        ["type"] = "set_variable",
        ["assignments"] = new JsonArray(new JsonObject { ["variable"] = variable, ["value"] = value }),
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    [Fact]
    public async Task SetVariable_ShippingPhoneFromBillingPhoneNode_SavesDigits()
    {
        var ctx = Ctx();
        ctx.FlowVars["billing_phone"] = """{"value":"5415551234","display_value":"(541) 555-1234","isTollFree":false}""";
        var addresses = new Mock<ICallAddressService>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), addresses.Object);

        await handler.ExecuteAsync(SetNode("{{call_record.shipping_phone}}", "{{flow.billing_phone}}"), ctx, null, "");

        addresses.Verify(a => a.SetPhoneAsync(ctx.CallRecordId, CallAddressRole.Shipping, "5415551234", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("5415551234", ctx.CallRecord["shipping_phone"]);
    }

    [Fact]
    public async Task SetVariable_PlainPhoneString_AndEmptyIgnored()
    {
        var addresses = new Mock<ICallAddressService>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), addresses.Object);
        var ctx = Ctx();

        await handler.ExecuteAsync(SetNode("call_record.billing_phone", "(800) 555-0100"), ctx, null, "");
        await handler.ExecuteAsync(SetNode("call_record.billing_phone", "{{flow.never_set}}"), ctx, null, "");

        addresses.Verify(a => a.SetPhoneAsync(ctx.CallRecordId, CallAddressRole.Billing, "8005550100", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{"value":"5415551234"}""", "5415551234")]
    [InlineData("(541) 555-1234", "5415551234")]
    [InlineData("""{"value":""}""", null)]
    [InlineData("", null)]
    [InlineData("{bad json", null)]
    public void PhoneDigits(string input, string? expected)
        => Assert.Equal(expected, CallAddressVars.PhoneDigits(input));
}
