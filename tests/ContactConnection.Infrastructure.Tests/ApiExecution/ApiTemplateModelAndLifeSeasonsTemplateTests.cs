using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.FlowEngine;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>
/// The Liquid template model, and the Life Seasons Order API body
/// (docs/integrations/life-seasons-order.liquid) rendered against it — checked field by field
/// against the contract recovered from CRMPro (docs/integrations/life-seasons-crmpro-legacy.md).
/// </summary>
public class ApiTemplateModelAndLifeSeasonsTemplateTests
{
    private static string LifeSeasonsTemplate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ContactConnection.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "integrations", "life-seasons-order.liquid"));
    }

    [Fact]
    public void Model_ExposesCartFeesPaymentAndAddressObjects_SkipsFlattenedKeys()
    {
        var model = ApiTemplateModelBuilder.Sample();

        Assert.Equal("LIFSEA-10000123", model["call_record"]!["order_number"]!.GetValue<string>());
        Assert.Equal("CO", model["call_record"]!["shipping_address"]!["state"]!.GetValue<string>());
        Assert.Equal("283-1-CTY-P10-SN", model["cart"]!["items"]![0]!["sku"]!.GetValue<string>());
        Assert.Equal("PF050714", model["cart"]!["items"]![0]!["tax_code"]!.GetValue<string>());
        Assert.Equal(0.28m, model["cart"]!["fees"]![0]!["amount"]!.GetValue<decimal>());
        Assert.Equal("60123456789", model["payment"]!["gateway_transaction_id"]!.GetValue<string>());
        Assert.Null(model["payment"]!["card_number"]); // never card data
    }

    [Fact]
    public void Model_FlowJsonValuesBecomeObjects_DottedCopiesSkipped()
    {
        var ctx = new FlowExecutionContext
        {
            FlowVars = new()
            {
                ["pay"] = """{"status":"approved"}""",
                ["pay.status"] = "approved",
                ["plain"] = "{not json",
            },
        };
        var model = ApiTemplateModelBuilder.Build(ctx, null, null, DateTimeOffset.UnixEpoch);

        Assert.Equal("approved", model["flow"]!["pay"]!["status"]!.GetValue<string>());
        Assert.Null(model["flow"]!["pay.status"]);
        Assert.Equal("{not json", model["flow"]!["plain"]!.GetValue<string>());
        Assert.Empty(model["cart"]!["items"]!.AsArray());
        Assert.Empty(model["payment"]!.AsObject());
    }

    [Fact]
    public async Task LifeSeasonsOrderTemplate_RendersTheLegacyContract()
    {
        var model = ApiTemplateModelBuilder.Sample();
        model["flow"]!["vendor_number"] = "VENDOR-1";
        model["flow"]!["keycode"] = "TV123";

        var rendered = await new FluidLiquidTemplateRenderer().RenderAsync(LifeSeasonsTemplate(), model);
        Assert.True(rendered.Success, rendered.Error);
        var order = JsonNode.Parse(rendered.Output!)!;   // must be valid JSON

        Assert.Equal("VENDOR-1", order["vendor_number"]!.GetValue<string>());
        Assert.Equal("LIFSEA-10000123", order["order_number"]!.GetValue<string>());
        Assert.Equal("LIFSEA-10000123", order["customer_number"]!.GetValue<string>());
        Assert.Equal("8005550100", order["dnis"]!.GetValue<string>());
        Assert.Equal("Jane", order["customer_info"]!["first_name"]!.GetValue<string>());
        Assert.Equal("3035550100", order["customer_info"]!["phone"]!.GetValue<string>());   // billing phone
        Assert.Equal("3035550199", order["shipping_info"]!["phone"]!.GetValue<string>());   // shipping phone
        Assert.Equal("1600 Broadway", order["billing_info"]!["address_1"]!.GetValue<string>());
        Assert.Equal("80202", order["shipping_info"]!["post_code"]!.GetValue<string>());
        Assert.Equal("US", order["shipping_info"]!["country_code"]!.GetValue<string>());

        var product = Assert.Single(order["products"]!.AsArray())!;
        Assert.Equal("283-1-CTY-P10-SN", product["ProductCode"]!.GetValue<string>());
        Assert.Equal(49.95m, product["Price"]!.GetValue<decimal>());
        Assert.Equal(1.45m, product["Tax"]!.GetValue<decimal>());

        Assert.Equal(49.95m, order["subtotal"]!.GetValue<decimal>());
        Assert.Equal(6.95m, order["shipping_cost"]!.GetValue<decimal>());
        Assert.Equal(1.65m, order["taxes"]!.GetValue<decimal>());           // fee NOT in taxes
        Assert.Equal(58.83m, order["total"]!.GetValue<decimal>());
        Assert.Equal("", order["coupon_code"]!.GetValue<string>());   // always sent, like production from 2026-06-05
        Assert.Equal("Denver", order["shipping_info"]!["city"]!.GetValue<string>());

        var fees = order["additional_fees_or_taxes"]!.AsArray();
        Assert.Equal(2, fees.Count);
        Assert.Equal("Shipping Tax", fees[0]!["Description"]!.GetValue<string>());
        Assert.Equal(0.20m, fees[0]!["Amount"]!.GetValue<decimal>());
        Assert.Equal("Retail Delivery Fee", fees[1]!["Description"]!.GetValue<string>());
        Assert.Equal(0.28m, fees[1]!["Amount"]!.GetValue<decimal>());

        Assert.Equal("auth", order["payment_info"]!["PaymentType"]!.GetValue<string>());
        Assert.Equal("60123456789", order["payment_info"]!["PaymentTransactionId"]!.GetValue<string>());
        Assert.Equal(58.83m, order["payment_info"]!["PaymentAmount"]!.GetValue<decimal>());

        var meta = order["CustomMeta"]!.AsObject();
        Assert.Equal("TV123", meta["mailin_keycode"]!.GetValue<string>());
        Assert.False(meta.ContainsKey("ls_referrer_firstname"));   // only what has values
    }

    [Fact]
    public async Task LifeSeasonsOrderTemplate_NoCustomData_OmitsCustomMeta_StillValidJson()
    {
        var rendered = await new FluidLiquidTemplateRenderer().RenderAsync(LifeSeasonsTemplate(), ApiTemplateModelBuilder.Sample());
        Assert.True(rendered.Success, rendered.Error);
        Assert.False(JsonNode.Parse(rendered.Output!)!.AsObject().ContainsKey("CustomMeta"));
    }

    [Fact]
    public async Task LifeSeasonsOrderTemplate_SmsAndReferrer_BuildsFullCustomMeta()
    {
        var model = ApiTemplateModelBuilder.Sample();
        model["flow"]!["sms_consent_at"] = "2026-09-27T17:00:00Z";
        model["flow"]!["sms_consent_marketing"] = "true";
        model["flow"]!["referrer_first_name"] = "Pat";

        var rendered = await new FluidLiquidTemplateRenderer().RenderAsync(LifeSeasonsTemplate(), model);
        Assert.True(rendered.Success, rendered.Error);
        var meta = JsonNode.Parse(rendered.Output!)!["CustomMeta"]!;
        Assert.False(meta.AsObject().ContainsKey("mailin_keycode"));
        Assert.True(meta["ls_consented_to_marketing_texts"]!.GetValue<bool>());
        Assert.False(meta["ls_consented_to_transactional_texts"]!.GetValue<bool>());
        Assert.Equal("Pat", meta["ls_referrer_firstname"]!.GetValue<string>());
    }
}
