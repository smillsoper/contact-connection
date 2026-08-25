using System.Security.Cryptography;
using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace ContactConnection.Api.Endpoints;

public static class AdminApiEndpointsEndpoints
{
    public static IEndpointRouteBuilder MapAdminApiEndpointsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/api-definitions/{definitionId:guid}/endpoints")
            .RequireAuthorization("TenantAdmin");

        group.MapGet("", GetAll);
        group.MapPost("", Create);
        group.MapPost("test", TestEndpoint);
        group.MapGet("{endpointId:guid}", GetById);
        group.MapPut("{endpointId:guid}", Update);
        group.MapPost("{endpointId:guid}/set-preferred", SetPreferred);
        group.MapDelete("{endpointId:guid}", Delete);
        group.MapGet("{endpointId:guid}/versions", ListVersions);
        group.MapPost("{endpointId:guid}/versions/{versionNumber:int}/revert", Revert);

        group.MapGet("{endpointId:guid}/webhook", GetWebhook);
        group.MapPost("{endpointId:guid}/webhook", EnableWebhook);
        group.MapPatch("{endpointId:guid}/webhook", UpdateWebhook);
        group.MapPost("{endpointId:guid}/webhook/regenerate-secret", RegenerateWebhookSecret);
        group.MapPost("{endpointId:guid}/webhook/regenerate-token", RegenerateWebhookToken);
        group.MapDelete("{endpointId:guid}/webhook", DisableWebhook);
        group.MapGet("{endpointId:guid}/webhook/events", ListWebhookEvents);

        return app;
    }

    private static async Task<IResult> GetAll(
        Guid definitionId,
        ITenantApiDefinitionRepository defRepo,
        ITenantApiEndpointRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var def = await defRepo.GetByIdAsync(definitionId, ct);
        if (def is null) return Results.NotFound();

        var endpoints = await repo.GetByDefinitionAsync(definitionId, ct);
        return Results.Ok(endpoints.Select(ToResponse));
    }

    private static async Task<IResult> GetById(
        Guid definitionId,
        Guid endpointId,
        ITenantApiEndpointRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> Create(
        Guid definitionId,
        CreateApiEndpointRequest request,
        ITenantApiDefinitionRepository defRepo,
        ITenantApiEndpointRepository repo,
        ITtsStreamProviderFactory ttsFactory,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        var def = await defRepo.GetByIdAsync(definitionId, ct);
        if (def is null) return Results.NotFound();

        if (def.ApiCategory != ApiCategory.General && !ApiSubType.IsValid(request.ApiSubType))
            return Results.BadRequest(new { error = $"Unknown api_sub_type '{request.ApiSubType}'. Valid sub-types: {string.Join(", ", ApiSubType.All)}" });

        if (request.ApiSubType == ApiSubType.TtsStreaming)
        {
            var error = TtsProviderValidation.Validate(def.Provider, ttsFactory);
            if (error is not null) return Results.BadRequest(new { error });
        }

        var endpoint = TenantApiEndpoint.Create(
            definitionId,
            def.ApiCategory,
            request.ApiSubType,
            request.Name,
            request.Path,
            request.HttpMethod,
            request.Description,
            request.SortOrder ?? 0);

        if (request.RequestBodyTemplate is not null) endpoint.SetRequestBodyTemplate(request.RequestBodyTemplate);
        if (request.QueryParams is not null) endpoint.SetQueryParams(request.QueryParams);
        if (request.Headers is not null) endpoint.SetHeaders(request.Headers);
        if (request.ResponseMapping is not null) endpoint.SetResponseMapping(request.ResponseMapping);
        if (request.IsRetrySafe is not null) endpoint.SetRetrySafe(request.IsRetrySafe.Value);

        await repo.AddAsync(endpoint, ct);
        await repo.SaveChangesAsync(ct);
        await versions.SnapshotAsync(
            VersionedEntityType.TenantApiEndpoint, endpoint.Id, BuildSnapshot(endpoint),
            actor.Value.Id, actor.Value.Name, "Created", ct);

        return Results.Created($"/api/v1/admin/api-definitions/{definitionId}/endpoints/{endpoint.Id}", ToResponse(endpoint));
    }

    private static async Task<IResult> Update(
        Guid definitionId,
        Guid endpointId,
        UpdateApiEndpointRequest request,
        ITenantApiDefinitionRepository defRepo,
        ITenantApiEndpointRepository repo,
        ITtsStreamProviderFactory ttsFactory,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var effectiveSubType = request.ApiSubType ?? endpoint.ApiSubType;
        var needsDefinition = request.ApiSubType is not null || effectiveSubType == ApiSubType.TtsStreaming;
        var def = needsDefinition ? await defRepo.GetByIdAsync(definitionId, ct) : null;
        if (needsDefinition && def is null) return Results.NotFound();

        if (effectiveSubType == ApiSubType.TtsStreaming)
        {
            var error = TtsProviderValidation.Validate(def!.Provider, ttsFactory);
            if (error is not null) return Results.BadRequest(new { error });
        }

        if (request.ApiSubType is not null)
        {
            if (def!.ApiCategory != ApiCategory.General && !ApiSubType.IsValid(request.ApiSubType))
                return Results.BadRequest(new { error = $"Unknown api_sub_type '{request.ApiSubType}'." });
            endpoint.UpdateSubType(def.ApiCategory, request.ApiSubType);
        }
        endpoint.Update(request.Name, request.Path, request.HttpMethod, request.Description, request.SortOrder);
        if (request.RequestBodyTemplate is not null) endpoint.SetRequestBodyTemplate(request.RequestBodyTemplate);
        if (request.QueryParams is not null) endpoint.SetQueryParams(request.QueryParams);
        if (request.Headers is not null) endpoint.SetHeaders(request.Headers);
        if (request.ResponseMapping is not null) endpoint.SetResponseMapping(request.ResponseMapping);
        if (request.IsRetrySafe is not null) endpoint.SetRetrySafe(request.IsRetrySafe.Value);

        await repo.SaveChangesAsync(ct);
        await versions.SnapshotAsync(
            VersionedEntityType.TenantApiEndpoint, endpoint.Id, BuildSnapshot(endpoint),
            actor.Value.Id, actor.Value.Name, "Updated", ct);
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> ListVersions(
        Guid definitionId,
        Guid endpointId,
        ITenantApiEndpointRepository repo,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();
        return Results.Ok(await versions.ListVersionsAsync(VersionedEntityType.TenantApiEndpoint, endpointId, ct));
    }

    private static async Task<IResult> Revert(
        Guid definitionId,
        Guid endpointId,
        int versionNumber,
        ITenantApiEndpointRepository repo,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var snapshotJson = await versions.GetSnapshotAsync(VersionedEntityType.TenantApiEndpoint, endpointId, versionNumber, ct);
        if (snapshotJson is null) return Results.NotFound(new { error = $"Version {versionNumber} not found." });

        var snapshot = JsonSerializer.Deserialize<ApiEndpointSnapshot>(snapshotJson)
            ?? throw new InvalidOperationException("Stored API endpoint snapshot is corrupt.");
        ApplySnapshot(endpoint, snapshot);

        await repo.SaveChangesAsync(ct);
        await versions.SnapshotAsync(
            VersionedEntityType.TenantApiEndpoint, endpoint.Id, BuildSnapshot(endpoint),
            actor.Value.Id, actor.Value.Name, $"Reverted to version {versionNumber}", ct);
        return Results.Ok(ToResponse(endpoint));
    }

    private static string BuildSnapshot(TenantApiEndpoint e) => JsonSerializer.Serialize(new ApiEndpointSnapshot(
        e.ApiSubType, e.Name, e.Description, e.Path, e.HttpMethod, e.RequestBodyTemplate,
        e.QueryParams, e.Headers, e.ResponseMapping, e.SortOrder, e.IsPreferred, e.IsActive, e.IsRetrySafe));

    // ApiSubType is deliberately not reverted — UpdateSubType needs the parent definition's
    // ApiCategory, which this revert path doesn't load, and sub-type changes post-creation are
    // rare in practice. Every other field is fully restored.
    private static void ApplySnapshot(TenantApiEndpoint e, ApiEndpointSnapshot s)
    {
        e.Update(s.Name, s.Path, s.HttpMethod, s.Description, s.SortOrder);
        e.SetRequestBodyTemplate(s.RequestBodyTemplate);
        e.SetQueryParams(s.QueryParams);
        e.SetHeaders(s.Headers);
        e.SetResponseMapping(s.ResponseMapping);
        e.SetRetrySafe(s.IsRetrySafe);
        if (s.IsActive) e.Activate(); else e.Deactivate();
        if (s.IsPreferred) e.SetPreferred(); else e.ClearPreferred();
    }

    private static async Task<IResult> SetPreferred(
        Guid definitionId,
        Guid endpointId,
        ITenantApiEndpointRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        await repo.ClearPreferredForSubTypeAsync(endpoint.ApiSubType, ct);
        endpoint.SetPreferred();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> TestEndpoint(
        Guid definitionId,
        RunEndpointTestRequest request,
        ITenantApiDefinitionRepository defRepo,
        ITenantCredentialStore credStore,
        IHttpClientFactory httpFactory,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var def = await defRepo.GetByIdAsync(definitionId, ct);
        if (def is null) return Results.NotFound();

        return await ApiEndpointTestHelper.RunTest(
            def.BaseUrl,
            def.AuthConfig,
            request,
            (key, token) => credStore.GetAsync(key, token),
            httpFactory,
            ct);
    }

    private static async Task<IResult> Delete(
        Guid definitionId,
        Guid endpointId,
        ITenantApiEndpointRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();
        await repo.DeleteAsync(endpoint, ct);
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── Webhook config ──────────────────────────────────────────────────────────────────────
    // See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support". Secrets are shown to
    // the admin exactly once, on enable/regenerate — never re-displayed after — matching the
    // reveal-once convention used elsewhere in this system for generated secrets.

    private static async Task<IResult> GetWebhook(
        Guid definitionId, Guid endpointId,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var webhook = await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct);
        if (webhook is null) return Results.NotFound();
        return Results.Ok(ToWebhookResponse(webhook, tenantContext));
    }

    private static async Task<IResult> EnableWebhook(
        Guid definitionId, Guid endpointId,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        ITenantCredentialStore credStore, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        if (await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct) is not null)
            return Results.BadRequest(new { error = "This endpoint already has a webhook configured." });

        var webhook = WebhookEndpoint.Create(endpointId);
        var secret = GenerateSecret();
        await credStore.SetAsync(webhook.CredentialKeyName, secret, ct: ct);
        await webhookRepo.AddAsync(webhook, ct);
        await webhookRepo.SaveChangesAsync(ct);

        return Results.Ok(ToWebhookResponse(webhook, tenantContext) with { Secret = secret });
    }

    private static async Task<IResult> UpdateWebhook(
        Guid definitionId, Guid endpointId, UpdateWebhookRequest request,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var webhook = await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct);
        if (webhook is null) return Results.NotFound();

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

        await webhookRepo.SaveChangesAsync(ct);
        return Results.Ok(ToWebhookResponse(webhook, tenantContext));
    }

    private static async Task<IResult> RegenerateWebhookSecret(
        Guid definitionId, Guid endpointId,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        ITenantCredentialStore credStore, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var webhook = await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct);
        if (webhook is null) return Results.NotFound();

        var secret = GenerateSecret();
        await credStore.SetAsync(webhook.CredentialKeyName, secret, ct: ct);
        return Results.Ok(ToWebhookResponse(webhook, tenantContext) with { Secret = secret });
    }

    private static async Task<IResult> RegenerateWebhookToken(
        Guid definitionId, Guid endpointId,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var webhook = await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct);
        if (webhook is null) return Results.NotFound();

        webhook.RegenerateToken();
        await webhookRepo.SaveChangesAsync(ct);
        return Results.Ok(ToWebhookResponse(webhook, tenantContext));
    }

    private static async Task<IResult> DisableWebhook(
        Guid definitionId, Guid endpointId,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        ITenantCredentialStore credStore, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var webhook = await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct);
        if (webhook is null) return Results.NotFound();

        await credStore.DeleteAsync(webhook.CredentialKeyName, ct);
        await webhookRepo.DeleteAsync(webhook, ct);
        await webhookRepo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListWebhookEvents(
        Guid definitionId, Guid endpointId, int? take,
        ITenantApiEndpointRepository repo, IWebhookEndpointRepository webhookRepo,
        IWebhookEventRepository eventRepo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        var webhook = await webhookRepo.GetByTenantApiEndpointIdAsync(endpointId, ct);
        if (webhook is null) return Results.NotFound();

        var events = await eventRepo.ListByEndpointAsync(webhook.Id, take ?? 50, ct);
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

    private static WebhookResponse ToWebhookResponse(WebhookEndpoint w, TenantContext tenantContext) => new(
        w.Id, w.TenantApiEndpointId, $"/api/v1/webhooks/{w.Token}", tenantContext.Current!.Subdomain,
        w.SignatureHeaderName, w.SignatureAlgorithm, w.IncludeTimestamp, w.TimestampToleranceSeconds,
        w.IsActive, w.CreatedAt, w.UpdatedAt, null);

    private static string GenerateSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static object ToResponse(TenantApiEndpoint e) => new
    {
        e.Id,
        e.DefinitionId,
        e.ApiSubType,
        e.Name,
        e.Description,
        e.Path,
        e.HttpMethod,
        e.RequestBodyTemplate,
        e.QueryParams,
        e.Headers,
        e.ResponseMapping,
        e.SortOrder,
        e.IsPreferred,
        e.IsActive,
        e.IsRetrySafe,
        e.CreatedAt,
        e.UpdatedAt,
    };
}

public record UpdateWebhookRequest(
    string? SignatureHeaderName,
    string? SignatureAlgorithm,
    bool? IncludeTimestamp,
    int? TimestampToleranceSeconds,
    bool? IsActive);

/// <summary>Secret is populated only by EnableWebhook/RegenerateWebhookSecret responses — the
/// reveal-once convention; every other response leaves it null.</summary>
public record WebhookResponse(
    Guid Id,
    Guid TenantApiEndpointId,
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
