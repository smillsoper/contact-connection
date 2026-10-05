using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Infrastructure.Credentials;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Campaign settings credential sections (S169) — Payment Gateways and the Sales Tax provider. Tenants never
/// type credential key names: each lists the registered vendors' fields with where each value currently
/// comes from (campaign / client / tenant / not set) and the exact key names; values are written through
/// the audited admin Credentials API. Secret values are never returned — only whether they're set. Test
/// checks the effective credentials with the vendor without charging or recording anything.
///
/// <c>?set=sandbox</c> (S179, script launch modes) reads and tests the campaign's SANDBOX set instead — keys
/// <c>{Vendor}.sandbox:...</c>, used by training and designer-sandbox runs. The sandbox set has no Environment field: it
/// always talks to the vendor's sandbox endpoint.
/// </summary>
public static class CampaignCredentialsEndpoints
{
    public static IEndpointRouteBuilder MapCampaignCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/campaigns/{id:guid}").RequireAuthorization("TenantAdmin");
        group.MapGet("payment-gateways", (Guid id, string? set, ICampaignRepository campaigns, IPaymentGatewayClientFactory gateways,
                ITenantCredentialStore credentials, TenantContext tenantContext, CancellationToken ct)
            => List(id, set, gateways.All, campaigns, credentials, tenantContext, ct));
        group.MapPost("payment-gateways/{provider}/test", (Guid id, string provider, string? set, ICampaignRepository campaigns,
                IPaymentGatewayClientFactory gateways, TenantContext tenantContext, CancellationToken ct)
            => Test(id, provider, set, gateways.All, campaigns, tenantContext, ct));
        group.MapGet("tax-providers", (Guid id, string? set, ICampaignRepository campaigns, IEnumerable<ITaxProvider> providers,
                ITenantCredentialStore credentials, TenantContext tenantContext, CancellationToken ct)
            => List(id, set, providers.OfType<ICampaignCredentialSet>(), campaigns, credentials, tenantContext, ct));
        group.MapPost("tax-providers/{provider}/test", (Guid id, string provider, string? set, ICampaignRepository campaigns,
                IEnumerable<ITaxProvider> providers, TenantContext tenantContext, CancellationToken ct)
            => Test(id, provider, set, providers.OfType<ICampaignCredentialSet>(), campaigns, tenantContext, ct));
        return app;
    }

    private static bool IsSandbox(string? set) => string.Equals(set, "sandbox", StringComparison.OrdinalIgnoreCase);

    private static async Task<IResult> List(Guid id, string? set, IEnumerable<ICampaignCredentialSet> sets, ICampaignRepository campaigns,
        ITenantCredentialStore credentials, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var sandbox = IsSandbox(set);
        var campaign = await campaigns.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();

        var result = new List<object>();
        foreach (var credentialSet in sets)
        {
            var d = credentialSet.Descriptor;
            var vendor = sandbox ? $"{d.CredentialVendor}.sandbox" : d.CredentialVendor;
            var fields = new List<object>();
            foreach (var f in d.Fields)
            {
                if (sandbox && f.Name == "Environment") continue;   // the sandbox set is always the sandbox endpoint
                var campaignKey = $"{vendor}:{campaign.Id}:{f.Name}";
                var clientKey = $"{vendor}:{campaign.ClientId}:{f.Name}";
                var tenantKey = $"{vendor}:{f.Name}";
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

    private static async Task<IResult> Test(Guid id, string provider, string? set, IEnumerable<ICampaignCredentialSet> sets,
        ICampaignRepository campaigns, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await campaigns.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        var credentialSet = sets.FirstOrDefault(s => string.Equals(s.Descriptor.ProviderKey, provider, StringComparison.OrdinalIgnoreCase));
        if (credentialSet is null) return Results.NotFound(new { error = $"Unknown provider '{provider}'." });
        using (CredentialSetScope.Use(IsSandbox(set) ? "sandbox" : null))
            return Results.Ok(await credentialSet.TestCredentialsAsync(campaign.Id, campaign.ClientId, ct));
    }
}
