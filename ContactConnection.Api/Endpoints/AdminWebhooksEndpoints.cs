using System.Security.Cryptography;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Admin CRUD for standalone inbound webhooks — create, view, edit (name/description/mapping
/// config/signature settings), rotate secret/token, disable/delete, and browse recent events.
/// A webhook is not tied to any TenantApiDefinition/TenantApiEndpoint (see WebhookEndpoint's own
/// doc comment) — this is a fully independent top-level resource. See
/// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support" (original endpoint-scoped design
/// shipped Session 87 with only a per-endpoint entry point and a Session 90 list-only dashboard;
/// replaced with this standalone canonical-object-mapping design, still Session 90, after a
/// pre-merge design review).
/// </summary>
public static class AdminWebhooksEndpoints
{
    public static IEndpointRouteBuilder MapAdminWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/webhooks")
            .RequireAuthorization("TenantAdmin");

        group.MapGet("", ListAll);
        group.MapPost("", Create);
        group.MapGet("{id:guid}", GetById);
        group.MapPatch("{id:guid}", Update);
        group.MapPost("{id:guid}/regenerate-secret", RegenerateSecret);
        group.MapPost("{id:guid}/regenerate-token", RegenerateToken);
        group.MapDelete("{id:guid}", Delete);
        group.MapGet("{id:guid}/events", ListEvents);

        return app;
    }

    private static async Task<IResult> ListAll(
        IWebhookEndpointRepository repo, IWebhookEventRepository eventRepo,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var webhooks = await repo.GetAllAsync(ct);
        var summaries = new List<AdminWebhookSummary>(webhooks.Count);
        // Small-N admin listing — a per-row last-event lookup is simpler and clearer than a
        // hand-rolled join query, matching the effort level of the rest of this dashboard.
        foreach (var w in webhooks)
        {
            var lastEvent = (await eventRepo.ListByEndpointAsync(w.Id, take: 1, ct)).FirstOrDefault();
            summaries.Add(new AdminWebhookSummary(
                w.Id, w.Name, w.Description, w.CanonicalType, $"/api/v1/webhooks/{w.Token}",
                w.IsActive, w.CreatedAt, w.UpdatedAt, lastEvent?.ReceivedAt, lastEvent?.ProcessingStatus));
        }
        return Results.Ok(summaries);
    }

    private static async Task<IResult> Create(
        CreateWebhookRequest request, IWebhookEndpointRepository repo, ITenantCredentialStore credStore,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanonicalWebhookType.IsValid(request.CanonicalType))
            return Results.BadRequest(new { error = $"Unknown canonical type '{request.CanonicalType}'. Valid types: {string.Join(", ", CanonicalWebhookType.All)}" });

        WebhookEndpoint webhook;
        try { webhook = WebhookEndpoint.Create(request.Name, request.CanonicalType, request.Description); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }

        if (request.MappingConfig is not null) webhook.SetMappingConfig(request.MappingConfig);
        if (request.SignatureHeaderName is not null || request.SignatureAlgorithm is not null
            || request.IncludeTimestamp is not null || request.TimestampToleranceSeconds is not null)
        {
            webhook.SetSignatureConfig(
                request.SignatureHeaderName ?? webhook.SignatureHeaderName,
                request.SignatureAlgorithm ?? webhook.SignatureAlgorithm,
                request.IncludeTimestamp ?? webhook.IncludeTimestamp,
                request.TimestampToleranceSeconds ?? webhook.TimestampToleranceSeconds);
        }

        var secret = GenerateSecret();
        await credStore.SetAsync(webhook.CredentialKeyName, secret, ct: ct);
        await repo.AddAsync(webhook, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/admin/webhooks/{webhook.Id}", ToResponse(webhook, tenantContext) with { Secret = secret });
    }

    private static async Task<IResult> GetById(
        Guid id, IWebhookEndpointRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var webhook = await repo.GetByIdAsync(id, ct);
        if (webhook is null) return Results.NotFound();
        return Results.Ok(ToResponse(webhook, tenantContext));
    }

    private static async Task<IResult> Update(
        Guid id, UpdateWebhookRequest request, IWebhookEndpointRepository repo,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var webhook = await repo.GetByIdAsync(id, ct);
        if (webhook is null) return Results.NotFound();

        if (request.Name is not null) webhook.Update(request.Name, request.Description ?? webhook.Description);
        if (request.MappingConfig is not null) webhook.SetMappingConfig(request.MappingConfig);
        if (request.SignatureHeaderName is not null || request.SignatureAlgorithm is not null
            || request.IncludeTimestamp is not null || request.TimestampToleranceSeconds is not null)
        {
            webhook.SetSignatureConfig(
                request.SignatureHeaderName ?? webhook.SignatureHeaderName,
                request.SignatureAlgorithm ?? webhook.SignatureAlgorithm,
                request.IncludeTimestamp ?? webhook.IncludeTimestamp,
                request.TimestampToleranceSeconds ?? webhook.TimestampToleranceSeconds);
        }
        if (request.IsActive is true) webhook.Activate();
        else if (request.IsActive is false) webhook.Deactivate();

        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(webhook, tenantContext));
    }

    private static async Task<IResult> RegenerateSecret(
        Guid id, IWebhookEndpointRepository repo, ITenantCredentialStore credStore,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var webhook = await repo.GetByIdAsync(id, ct);
        if (webhook is null) return Results.NotFound();

        var secret = GenerateSecret();
        await credStore.SetAsync(webhook.CredentialKeyName, secret, ct: ct);
        return Results.Ok(ToResponse(webhook, tenantContext) with { Secret = secret });
    }

    private static async Task<IResult> RegenerateToken(
        Guid id, IWebhookEndpointRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var webhook = await repo.GetByIdAsync(id, ct);
        if (webhook is null) return Results.NotFound();

        webhook.RegenerateToken();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(webhook, tenantContext));
    }

    private static async Task<IResult> Delete(
        Guid id, IWebhookEndpointRepository repo, ITenantCredentialStore credStore,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var webhook = await repo.GetByIdAsync(id, ct);
        if (webhook is null) return Results.NotFound();

        await credStore.DeleteAsync(webhook.CredentialKeyName, ct);
        await repo.DeleteAsync(webhook, ct);
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListEvents(
        Guid id, int? take, IWebhookEndpointRepository repo, IWebhookEventRepository eventRepo,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var webhook = await repo.GetByIdAsync(id, ct);
        if (webhook is null) return Results.NotFound();

        var events = await eventRepo.ListByEndpointAsync(id, take ?? 50, ct);
        return Results.Ok(events.Select(e => new
        {
            e.Id,
            e.ReceivedAt,
            e.SignatureValid,
            e.ProcessingStatus,
            e.ProcessingError,
            e.OutcomeKey,
            e.ProcessedAt,
        }));
    }

    private static WebhookResponse ToResponse(WebhookEndpoint w, TenantContext tenantContext) => new(
        w.Id, w.Name, w.Description, w.CanonicalType, w.MappingConfig, $"/api/v1/webhooks/{w.Token}",
        tenantContext.Current!.Subdomain, w.SignatureHeaderName, w.SignatureAlgorithm, w.IncludeTimestamp,
        w.TimestampToleranceSeconds, w.IsActive, w.CreatedAt, w.UpdatedAt, null);

    private static string GenerateSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}

public record CreateWebhookRequest(
    string Name,
    string CanonicalType,
    string? Description = null,
    string? MappingConfig = null,
    string? SignatureHeaderName = null,
    string? SignatureAlgorithm = null,
    bool? IncludeTimestamp = null,
    int? TimestampToleranceSeconds = null);

public record UpdateWebhookRequest(
    string? Name,
    string? Description,
    string? MappingConfig,
    string? SignatureHeaderName,
    string? SignatureAlgorithm,
    bool? IncludeTimestamp,
    int? TimestampToleranceSeconds,
    bool? IsActive);

/// <summary>Secret is populated only by Create/RegenerateSecret responses — reveal-once, never
/// re-displayed after, matching the convention used elsewhere in this system for generated
/// secrets.</summary>
public record WebhookResponse(
    Guid Id,
    string Name,
    string? Description,
    string CanonicalType,
    string MappingConfig,
    string Path,
    string TenantSubdomain,
    string SignatureHeaderName,
    string SignatureAlgorithm,
    bool IncludeTimestamp,
    int TimestampToleranceSeconds,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Secret);

public record AdminWebhookSummary(
    Guid Id,
    string Name,
    string? Description,
    string CanonicalType,
    string Url,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastEventAt,
    string? LastEventStatus);
