using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Public inbound-webhook receiver — vendors push events here (e.g. fulfillment tracking
/// updates) instead of us only ever polling/calling out. Authenticated via a per-endpoint HMAC
/// signature (see HmacSigner.VerifySignatureHeaderValue), not bearer JWT — this route is
/// deliberately AllowAnonymous. Tenant identification relies entirely on the existing
/// TenantResolutionMiddleware (host-header subdomain, same as every other tenant-scoped
/// request) — no new tenant-resolution mechanism. See API_HARDENING_CHECKLIST.md Tier 2,
/// "Inbound webhook support".
/// </summary>
public static class WebhooksEndpoints
{
    public static IEndpointRouteBuilder MapWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/{token}", Receive).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> Receive(
        string token,
        HttpContext http,
        IWebhookEndpointRepository webhookEndpointRepo,
        ITenantApiEndpointRepository apiEndpointRepo,
        ITenantCredentialStore credStore,
        IWebhookEventRepository eventRepo,
        IOrderRepository orderRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        // Don't leak whether a token exists cross-tenant — a vendor that hits the wrong
        // subdomain (or none) gets the same 404 as an unknown token.
        if (!tenantContext.HasTenant) return Results.NotFound();

        var webhookEndpoint = await webhookEndpointRepo.GetByTokenAsync(token, ct);
        if (webhookEndpoint is null || !webhookEndpoint.IsActive) return Results.NotFound();

        var apiEndpoint = await apiEndpointRepo.GetByIdAsync(webhookEndpoint.TenantApiEndpointId, ct);
        if (apiEndpoint is null) return Results.NotFound();

        http.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(http.Request.Body, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);
        http.Request.Body.Position = 0;

        var secret = await credStore.GetAsync(webhookEndpoint.CredentialKeyName, ct);
        var signatureHeader = http.Request.Headers[webhookEndpoint.SignatureHeaderName].FirstOrDefault();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, rawBody, http.Request.ContentType, signatureHeader, secret,
            eventRepo, orderRepo, ct);

        return result.Outcome switch
        {
            WebhookReceiveHandler.ReceiveOutcome.InvalidSignature => Results.Unauthorized(),
            // Duplicate and Accepted both ack fast with 2xx — a vendor retrying an already-
            // handled delivery should not be told to keep retrying, and a processing failure
            // (bad mapping, unknown order line) has already been durably logged, not lost.
            _ => Results.Ok(),
        };
    }
}
