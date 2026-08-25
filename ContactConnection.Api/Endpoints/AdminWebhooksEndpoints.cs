using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Tenant-wide Webhooks dashboard — lists every configured webhook across every API Definition/
/// Endpoint in one place, rather than requiring an admin to know which specific endpoint a
/// webhook lives under to find it. The per-webhook config itself (URL, secret, signature
/// settings, events log) stays on AdminApiEndpointsEndpoints' existing endpoint-scoped routes —
/// this only adds the missing "where do I even find my webhooks" list view. See
/// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support" (originally shipped Session 87
/// with only a per-endpoint entry point; this dashboard view added Session 90 after a design
/// review before merging).
/// </summary>
public static class AdminWebhooksEndpoints
{
    public static IEndpointRouteBuilder MapAdminWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/webhooks", ListAll)
            .RequireAuthorization("TenantAdmin");

        return app;
    }

    private static async Task<IResult> ListAll(
        IWebhookEndpointRepository webhookRepo,
        ITenantApiEndpointRepository endpointRepo,
        ITenantApiDefinitionRepository defRepo,
        IWebhookEventRepository eventRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var webhooks = await webhookRepo.GetAllAsync(ct);
        var summaries = new List<AdminWebhookSummary>(webhooks.Count);

        // Small-N admin listing (realistically a handful of webhooks per tenant, not thousands) —
        // a per-row lookup is simpler and clearer than a hand-rolled join query, matching the
        // effort level of the rest of this dashboard's read paths.
        foreach (var webhook in webhooks)
        {
            var endpoint = await endpointRepo.GetByIdAsync(webhook.TenantApiEndpointId, ct);
            if (endpoint is null) continue; // orphaned webhook (endpoint deleted) — shouldn't happen, skip defensively

            var def = await defRepo.GetByIdAsync(endpoint.DefinitionId, ct);
            var lastEvent = (await eventRepo.ListByEndpointAsync(webhook.Id, take: 1, ct)).FirstOrDefault();

            summaries.Add(new AdminWebhookSummary(
                WebhookEndpointId: webhook.Id,
                DefinitionId: endpoint.DefinitionId,
                DefinitionName: def?.Name ?? "(deleted definition)",
                EndpointId: endpoint.Id,
                EndpointName: endpoint.Name,
                EndpointPath: endpoint.Path,
                Url: $"/api/v1/webhooks/{webhook.Token}",
                IsActive: webhook.IsActive,
                CreatedAt: webhook.CreatedAt,
                UpdatedAt: webhook.UpdatedAt,
                LastEventAt: lastEvent?.ReceivedAt,
                LastEventStatus: lastEvent?.ProcessingStatus));
        }

        return Results.Ok(summaries);
    }
}

public record AdminWebhookSummary(
    Guid WebhookEndpointId,
    Guid DefinitionId,
    string DefinitionName,
    Guid EndpointId,
    string EndpointName,
    string EndpointPath,
    string Url,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastEventAt,
    string? LastEventStatus);
