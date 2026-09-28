using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Common;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// External routing API — lets an external router (RingSquared, or any partner) ask whether we'll
/// take a call before sending it, and report calls it placed. docs/design/parallel-queuing.md.
///
/// Called at the tenant's own host ({tenant}.contactconnection.io — TenantResolutionMiddleware), and
/// authenticated by a NumberProvider API key (issued on the Number Providers screen) in the
/// <c>X-Api-Key</c> header, or an <c>api_key</c> query parameter for routers that can only
/// configure a URL. No user session. Campaign ids are ours; <c>?group=</c> asks about one agent
/// group only (Elite).
///
///   POST {campaignId}/Routing       RingSquared contract (TMS Dial800Routing): {SessionId, DNIS, ANI}
///                                   → 200 {Targets:[delivery number]} | 404 {Targets:[], Message}
///   POST CallInfo                   {CallData:{ClientID, CallDate, Dialed_TFN, ANI}} → {Success, Message}
///   GET  {campaignId}/availability  live counts (logged in / available / queued / longest wait, per tier)
///   GET  {campaignId}/available     200 {available:true} | 404 {available:false} — the same accept
///                                   rule as Routing, without the delivery-number lookup
/// </summary>
public static class ExternalRoutingEndpoints
{
    // The RingSquared contract is PascalCase (WCF DataContract names) — the API-wide camelCase
    // policy must not rename these.
    private static readonly JsonSerializerOptions ContractJson = new() { PropertyNamingPolicy = null };

    public static IEndpointRouteBuilder MapExternalRoutingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/external-routing").AllowAnonymous()
            .AddEndpointFilter(RequireProviderKey);

        group.MapPost("{campaignId:guid}/Routing", Routing);
        group.MapPost("CallInfo", CallInfo);
        group.MapGet("{campaignId:guid}/availability", Availability);
        group.MapGet("{campaignId:guid}/available", Available);

        return app;
    }

    private const string ProviderItem = "ExternalRoutingProvider";

    /// <summary>Tenant from the host, provider from the API key. A missing/unknown key and an
    /// unknown tenant both get the same 401 so the endpoint doesn't reveal which tenants exist.</summary>
    private static async ValueTask<object?> RequireProviderKey(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var tenant = http.RequestServices.GetRequiredService<TenantContext>();
        var key = http.Request.Headers["X-Api-Key"].FirstOrDefault() ?? http.Request.Query["api_key"].FirstOrDefault();
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(key))
            return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);

        var providers = http.RequestServices.GetRequiredService<INumberProviderRepository>();
        var provider = await providers.GetActiveByApiKeyHashAsync(ApiKeyHasher.Hash(key.Trim()), http.RequestAborted);
        if (provider is null)
            return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);

        http.Items[ProviderItem] = provider;
        return await next(ctx);
    }

    private static NumberProvider Provider(HttpContext http) => (NumberProvider)http.Items[ProviderItem]!;

    // ── POST {campaignId}/Routing ────────────────────────────────────────────
    private static async Task<IResult> Routing(
        Guid campaignId, Guid? group, JsonElement body, HttpContext http,
        TenantContext tenant, ScopedTenantDbContextFactory dbFactory, ExternalRoutingService routing, CancellationToken ct)
    {
        var provider = Provider(http);
        var sessionId = Field(body, "SessionId");
        var dnis = ExternalRoutingService.NormalizeNumber(Field(body, "DNIS"));
        var ani = ExternalRoutingService.NormalizeNumber(Field(body, "ANI"));

        await using var db = dbFactory.Create();
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
            return Results.Json(new { Targets = Array.Empty<string>(), Message = "Unknown campaign" }, ContractJson, statusCode: 404);

        // Target = our delivery number(s) standing for the dialed client TFN, for this provider.
        var deliveryNumbers = await db.PhoneNumbers
            .Where(p => p.ProviderId == provider.Id && p.Role == PhoneNumberRole.RoutingDelivery && p.IsActive && p.ClientNumber != null)
            .Select(p => new { p.Number, p.ClientNumber })
            .ToListAsync(ct);
        var targets = deliveryNumbers
            .Where(p => ExternalRoutingService.NormalizeNumber(p.ClientNumber) == dnis)
            .Select(p => ExternalRoutingService.NormalizeNumber(p.Number))
            .Distinct()
            .ToList();

        RoutingDecision decision;
        if (targets.Count == 0)
            decision = new RoutingDecision(false, "Number not configured");
        else
        {
            var availability = await routing.GetAvailabilityAsync(db, tenant.Current!.Id, campaignId, group, ct);
            decision = ExternalRoutingService.Decide(campaign.ExternalRoutingAcceptMode, campaign.ExternalRoutingLimit, availability);
        }

        // Every decision is logged — rejects are what reporting needs (TMS wrote them to its CDR).
        db.ExternalRoutingRequests.Add(ExternalRoutingRequest.Routing(
            tenant.Current!.Id, provider.Id, campaignId, group, sessionId, dnis, ani,
            decision.Accepted, decision.Reason, decision.Accepted ? targets : []));
        await db.SaveChangesAsync(ct);

        return decision.Accepted
            ? Results.Json(new { Targets = targets }, ContractJson)
            : Results.Json(new { Targets = Array.Empty<string>(), Message = decision.Reason }, ContractJson, statusCode: 404);
    }

    // ── POST CallInfo ────────────────────────────────────────────────────────
    // WCF wrapped-request shape {"CallData": {...}}; a bare object is accepted too.
    private static async Task<IResult> CallInfo(
        JsonElement body, HttpContext http, TenantContext tenant, ScopedTenantDbContextFactory dbFactory, CancellationToken ct)
    {
        var data = TryGet(body, "CallData", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : body;
        if (data.ValueKind != JsonValueKind.Object)
            return Results.Json(new { Success = false, Message = "CallData required" }, ContractJson, statusCode: 400);

        await using var db = dbFactory.Create();
        db.ExternalRoutingRequests.Add(ExternalRoutingRequest.CallInfo(
            tenant.Current!.Id, Provider(http).Id,
            Field(data, "ClientID"), Field(data, "CallDate"),
            ExternalRoutingService.NormalizeNumber(Field(data, "Dialed_TFN")),
            ExternalRoutingService.NormalizeNumber(Field(data, "ANI"))));
        await db.SaveChangesAsync(ct);

        return Results.Json(new { Success = true, Message = "" }, ContractJson);
    }

    // ── GET {campaignId}/availability ────────────────────────────────────────
    private static async Task<IResult> Availability(
        Guid campaignId, Guid? group, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        ExternalRoutingService routing, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null) return Results.NotFound(new { error = "Unknown campaign" });

        var a = await routing.GetAvailabilityAsync(db, tenant.Current!.Id, campaignId, group, ct);
        var decision = ExternalRoutingService.Decide(campaign.ExternalRoutingAcceptMode, campaign.ExternalRoutingLimit, a);
        return Results.Ok(new
        {
            campaignId, campaignName = campaign.Name, agentGroupId = group,
            a.LoggedIn, a.Available, a.Unavailable, a.Queued, a.LongestWaitSeconds,
            wouldAccept = decision.Accepted,
            a.Tiers,
            asOf = DateTimeOffset.UtcNow,
        });
    }

    // ── GET {campaignId}/available ───────────────────────────────────────────
    private static async Task<IResult> Available(
        Guid campaignId, Guid? group, TenantContext tenant, ScopedTenantDbContextFactory dbFactory,
        ExternalRoutingService routing, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null) return Results.NotFound(new { available = false });

        var a = await routing.GetAvailabilityAsync(db, tenant.Current!.Id, campaignId, group, ct);
        return ExternalRoutingService.Decide(campaign.ExternalRoutingAcceptMode, campaign.ExternalRoutingLimit, a).Accepted
            ? Results.Ok(new { available = true })
            : Results.NotFound(new { available = false });
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        return false;
    }

    private static string? Field(JsonElement obj, string name) =>
        TryGet(obj, name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;
}
