using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Campaign settings credential sections (S169) — Payment Gateways and the Sales Tax provider. Tenants never
/// type credential key names: each lists the registered vendors' fields with where each value currently
/// comes from (campaign / client / tenant / not set) and the exact key names; values are written through
/// the audited admin Credentials API. Secret values are never returned — only whether they're set. Test
/// checks the effective credentials with the vendor without charging or recording anything.
/// </summary>
public static class CampaignCredentialsEndpoints
{
    public static IEndpointRouteBuilder MapCampaignCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/campaigns/{id:guid}").RequireAuthorization("TenantAdmin");
        group.MapGet("payment-gateways", (Guid id, ICampaignRepository campaigns, IPaymentGatewayClientFactory gateways,
                ITenantCredentialStore credentials, TenantContext tenantContext, CancellationToken ct)
            => List(id, gateways.All, campaigns, credentials, tenantContext, ct));
        group.MapPost("payment-gateways/{provider}/test", (Guid id, string provider, ICampaignRepository campaigns,
                IPaymentGatewayClientFactory gateways, TenantContext tenantContext, CancellationToken ct)
            => Test(id, provider, gateways.All, campaigns, tenantContext, ct));
        group.MapGet("tax-providers", (Guid id, ICampaignRepository campaigns, IEnumerable<ITaxProvider> providers,
                ITenantCredentialStore credentials, TenantContext tenantContext, CancellationToken ct)
            => List(id, providers.OfType<ICampaignCredentialSet>(), campaigns, credentials, tenantContext, ct));
        group.MapPost("tax-providers/{provider}/test", (Guid id, string provider, ICampaignRepository campaigns,
                IEnumerable<ITaxProvider> providers, TenantContext tenantContext, CancellationToken ct)
            => Test(id, provider, providers.OfType<ICampaignCredentialSet>(), campaigns, tenantContext, ct));
        return app;
    }

    private static async Task<IResult> List(Guid id, IEnumerable<ICampaignCredentialSet> sets, ICampaignRepository campaigns,
        ITenantCredentialStore credentials, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await campaigns.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();

        var result = new List<object>();
        foreach (var set in sets)
        {
            var d = set.Descriptor;
            var fields = new List<object>();
            foreach (var f in d.Fields)
            {
                var campaignKey = $"{d.CredentialVendor}:{campaign.Id}:{f.Name}";
                var clientKey = $"{d.CredentialVendor}:{campaign.ClientId}:{f.Name}";
                var tenantKey = $"{d.CredentialVendor}:{f.Name}";
                string? source = null, effective = null;
                foreach (var (scope, key) in new[] { ("campaign", campaignKey), ("client", clientKey), ("tenant", tenantKey) })
                {
                    if (await credentials.GetAsync(key, ct) is { } value)
                    {
                        source = scope;
                        effective = f.Secret ? null : value;   // never return a secret
                        break;
                    }
                }
                fields.Add(new
                {
                    f.Name, f.Label, f.Secret, f.Options, f.Default, f.Help,
                    source,
                    value = effective ?? (f.Secret ? null : f.Default),
                    keys = new { campaign = campaignKey, client = clientKey, tenant = tenantKey },
                });
            }
            result.Add(new { d.ProviderKey, d.DisplayName, d.CredentialVendor, d.Instructions, fields });
        }
        return Results.Ok(result);
    }

    private static async Task<IResult> Test(Guid id, string provider, IEnumerable<ICampaignCredentialSet> sets,
        ICampaignRepository campaigns, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await campaigns.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        var set = sets.FirstOrDefault(s => string.Equals(s.Descriptor.ProviderKey, provider, StringComparison.OrdinalIgnoreCase));
        if (set is null) return Results.NotFound(new { error = $"Unknown provider '{provider}'." });
        return Results.Ok(await set.TestCredentialsAsync(campaign.Id, campaign.ClientId, ct));
    }
}
