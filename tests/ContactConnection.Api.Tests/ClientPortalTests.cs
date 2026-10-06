using System.Security.Claims;
using ContactConnection.Api.Endpoints;
using ContactConnection.Api.Middleware;
using Xunit;

namespace ContactConnection.Api.Tests;

/// <summary>S181 client portal: what a client user is served, and tenant isolation for every signed-in token.</summary>
public class ClientPortalTests
{
    [Fact]
    public void Client_layout_keeps_report_widgets_only_and_strips_internal_filters()
    {
        var layout = """
            [{"id":"a","widgetType":"kpi","x":0,"y":0,"w":8,"h":7,"config":{"clientId":"c","campaignId":"x","groupBy":"day","kpis":["orders"]}},
             {"id":"b","widgetType":"agent_list","x":0,"y":7,"w":4,"h":8,"config":{}},
             {"id":"c","widgetType":"active_calls","config":{}},
             {"id":"d","widgetType":"service_level_threshold","config":{"groupId":"g","loggedInOnly":true,"timeWindow":{"mode":"today"}}}]
            """;
        var result = ClientPortalEndpoints.ClientLayout(layout);

        Assert.Equal(["a", "d"], result.Select(w => w!["id"]!.GetValue<string>()));
        var kpi = result[0]!["config"]!.AsObject();
        Assert.False(kpi.ContainsKey("clientId"));
        Assert.False(kpi.ContainsKey("campaignId"));
        Assert.Equal("day", kpi["groupBy"]!.GetValue<string>());
        var sl = result[1]!["config"]!.AsObject();
        Assert.False(sl.ContainsKey("groupId"));
        Assert.False(sl.ContainsKey("loggedInOnly"));
        Assert.True(sl.ContainsKey("timeWindow"));
    }

    [Fact]
    public void Bad_layout_json_serves_nothing()
    {
        Assert.Empty(ClientPortalEndpoints.ClientLayout("not json"));
    }

    private static ClaimsPrincipal Signed(params (string, string)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Item1, c.Item2)), "Bearer"));

    [Fact]
    public void A_token_for_one_tenant_cannot_be_used_against_another()
    {
        var tenant = Guid.NewGuid();
        Assert.False(TenantResolutionMiddleware.TokenTenantMismatch(Signed(("tenant_id", tenant.ToString())), tenant));
        Assert.True(TenantResolutionMiddleware.TokenTenantMismatch(Signed(("tenant_id", Guid.NewGuid().ToString())), tenant));
        // Platform (portal) tokens carry no tenant claim; anonymous requests carry nothing.
        Assert.False(TenantResolutionMiddleware.TokenTenantMismatch(Signed(("role", "platform_admin")), tenant));
        Assert.False(TenantResolutionMiddleware.TokenTenantMismatch(new ClaimsPrincipal(new ClaimsIdentity()), tenant));
    }
}
