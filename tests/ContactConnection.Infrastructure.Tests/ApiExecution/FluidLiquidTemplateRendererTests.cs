using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.ApiExecution;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

public class FluidLiquidTemplateRendererTests
{
    private static readonly FluidLiquidTemplateRenderer Sut = new();

    private static JsonObject Model() => JsonNode.Parse("""
        {
          "caller": { "first_name": "Jo \"JJ\" O'Neil" },
          "cart": {
            "subtotal": 59.95, "sales_tax": 1.89,
            "items": [
              { "sku": "283-1-CTY-P10-SN", "quantity": 1, "full_price": 49.95, "sales_tax": 1.45 },
              { "sku": "RETAIL DELIVERY FEE", "quantity": 1, "full_price": 0, "sales_tax": 0 },
              { "sku": "GIFT", "quantity": 2, "full_price": 5, "sales_tax": 0.44 }
            ]
          }
        }
        """)!.AsObject();

    [Fact]
    public async Task JsonFilter_QuotesAndEscapes_AndOutputIsNotHtmlEncoded()
    {
        var r = await Sut.RenderAsync("""{"name": {{ caller.first_name | json }}}""", Model());
        Assert.True(r.Success, r.Error);
        Assert.Equal("{\"name\": \"Jo \\u0022JJ\\u0022 O\\u0027Neil\"}", r.Output);
        Assert.Equal("Jo \"JJ\" O'Neil", JsonNode.Parse(r.Output!)!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task LoopConditionalAndMath_ProduceValidJson()
    {
        const string template = """
            {% assign n = 0 %}
            {"products": [{% for item in cart.items %}{% unless item.sku == "RETAIL DELIVERY FEE" %}{% if n > 0 %},{% endif %}{% assign n = n | plus: 1 %}
              {"ProductCode": {{ item.sku | json }}, "Quantity": {{ item.quantity }}, "Price": {{ item.full_price | money }}, "Tax": {{ item.sales_tax | money }}}{% endunless %}{% endfor %}],
             "taxes": {{ cart.sales_tax | minus: 0.29 | money }}}
            """;
        var r = await Sut.RenderAsync(template, Model());
        Assert.True(r.Success, r.Error);

        var json = JsonNode.Parse(r.Output!)!;
        var products = json["products"]!.AsArray();
        Assert.Equal(2, products.Count);
        Assert.Equal("283-1-CTY-P10-SN", products[0]!["ProductCode"]!.GetValue<string>());
        Assert.Equal(49.95m, products[0]!["Price"]!.GetValue<decimal>());
        Assert.Equal(5.00m, products[1]!["Price"]!.GetValue<decimal>());
        Assert.Equal(1.60m, json["taxes"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Money_AlwaysTwoDecimals_HalfUp()
    {
        var r = await Sut.RenderAsync("{{ a | money }}|{{ b | money }}|{{ c | money }}",
            JsonNode.Parse("""{"a": 5, "b": 0.145, "c": 1234.5}""")!.AsObject());
        Assert.Equal("5.00|0.15|1234.50", r.Output);
    }

    [Fact]
    public void Validate_ReportsSyntaxErrors_AcceptsGoodTemplates()
    {
        Assert.Null(Sut.Validate("{% for i in cart.items %}{{ i.sku }}{% endfor %}"));
        Assert.NotNull(Sut.Validate("{% for i in cart.items %}{{ i.sku }}"));  // unclosed for
        Assert.NotNull(Sut.Validate("{{ cart.items | }}"));
    }

    [Fact]
    public async Task RenderAsync_BrokenTemplate_FailsWithoutThrowing()
    {
        var r = await Sut.RenderAsync("{% if %}", Model());
        Assert.False(r.Success);
        Assert.StartsWith("Liquid template error", r.Error);
    }

    [Fact]
    public async Task RunawayLoop_IsStoppedByStepLimit()
    {
        var r = await Sut.RenderAsync("{% for i in (1..10000000) %}x{% endfor %}", Model());
        Assert.False(r.Success);
        Assert.StartsWith("Liquid render error", r.Error);
    }

    [Fact]
    public async Task InMemoryNumericNodes_OfAnyClrType_Render()
    {
        // Models built in code (not parsed from text) hold JsonValue<int>/<long>/<decimal>/<double>.
        var model = new JsonObject { ["i"] = 3, ["l"] = 4L, ["m"] = 1.5m, ["d"] = 2.25d };
        var r = await Sut.RenderAsync("{{ i | plus: l }}|{{ m | money }}|{{ d | money }}", model);
        Assert.True(r.Success, r.Error);
        Assert.Equal("7|1.50|2.25", r.Output);
    }

    [Fact]
    public async Task MissingValues_RenderEmpty_NotErrors()
    {
        var r = await Sut.RenderAsync("[{{ nope.nothing }}]", Model());
        Assert.True(r.Success);
        Assert.Equal("[]", r.Output);
    }
}
