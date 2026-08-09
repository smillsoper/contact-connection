using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace ContactConnection.Api.Endpoints;

public static class AdminApiDefinitionsEndpoints
{
    public static IEndpointRouteBuilder MapAdminApiDefinitionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/api-definitions")
            .RequireAuthorization("TenantAdmin");

        group.MapGet("", GetAll);
        group.MapPost("", Create);
        group.MapPost("test-auth", TestAuth);
        group.MapGet("{id:guid}", GetById);
        group.MapPut("{id:guid}", Update);
        group.MapPost("{id:guid}/activate", Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);
        group.MapDelete("{id:guid}", Delete);
        group.MapGet("{id:guid}/versions", ListVersions);
        group.MapPost("{id:guid}/versions/{versionNumber:int}/revert", Revert);

        return app;
    }

    private static async Task<IResult> GetAll(
        ITenantApiDefinitionRepository repo,
        TenantContext tenantContext,
        string? category,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var all = category is null
            ? await repo.GetAllAsync(ct)
            : await repo.GetByCategoryAsync(category, ct);
        return Results.Ok(all.Select(ToResponse));
    }

    private static async Task<IResult> GetById(
        Guid id,
        ITenantApiDefinitionRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var def = await repo.GetByIdAsync(id, ct);
        return def is null ? Results.NotFound() : Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Create(
        CreateApiDefinitionRequest request,
        ITenantApiDefinitionRepository repo,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        if (!ApiCategory.IsValid(request.ApiCategory))
            return Results.BadRequest(new { error = $"Unknown api_category '{request.ApiCategory}'. Valid categories: {string.Join(", ", ApiCategory.All)}" });

        var def = TenantApiDefinition.Create(request.ApiCategory, request.Name, request.HttpMethod, request.BaseUrl, request.Description, request.Provider, request.TimeoutSeconds ?? 30);
        if (request.AuthConfig is not null) def.SetAuthConfig(request.AuthConfig);
        await repo.AddAsync(def, ct);
        await repo.SaveChangesAsync(ct);
        await versions.SnapshotAsync(
            VersionedEntityType.TenantApiDefinition, def.Id, BuildSnapshot(def),
            actor.Value.Id, actor.Value.Name, "Created", ct);

        return Results.Created($"/api/v1/admin/api-definitions/{def.Id}", ToResponse(def));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateApiDefinitionRequest request,
        ITenantApiDefinitionRepository repo,
        ITenantApiEndpointRepository endpointRepo,
        ITtsStreamProviderFactory ttsFactory,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();

        if (request.ApiCategory is not null)
        {
            if (!ApiCategory.IsValid(request.ApiCategory))
                return Results.BadRequest(new { error = $"Unknown api_category '{request.ApiCategory}'." });
            def.UpdateCategory(request.ApiCategory);
        }

        // Provider doubles as a runtime dispatch key only for definitions backing a TtsStreaming
        // endpoint (see TtsProviderValidation) — a tenant registering their own TTS vendor
        // account is subject to the same constraint as the platform catalog.
        if (request.Provider is not null)
        {
            var endpoints = await endpointRepo.GetByDefinitionAsync(id, ct);
            if (endpoints.Any(e => e.ApiSubType == ApiSubType.TtsStreaming))
            {
                var error = TtsProviderValidation.Validate(request.Provider, ttsFactory);
                if (error is not null) return Results.BadRequest(new { error });
            }
        }

        def.Update(request.Name, request.HttpMethod, request.BaseUrl, request.Description, request.Provider, request.TimeoutSeconds);
        if (request.Headers is not null) def.SetHeaders(request.Headers);
        if (request.QueryParams is not null) def.SetQueryParams(request.QueryParams);
        if (request.RequestBodyTemplate is not null) def.SetRequestBodyTemplate(request.RequestBodyTemplate);
        if (request.ResponseMapping is not null) def.SetResponseMapping(request.ResponseMapping);
        if (request.AuthConfig is not null) def.SetAuthConfig(request.AuthConfig);

        await repo.SaveChangesAsync(ct);
        await versions.SnapshotAsync(
            VersionedEntityType.TenantApiDefinition, def.Id, BuildSnapshot(def),
            actor.Value.Id, actor.Value.Name, "Updated", ct);
        return Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> ListVersions(
        Guid id,
        ITenantApiDefinitionRepository repo,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();
        return Results.Ok(await versions.ListVersionsAsync(VersionedEntityType.TenantApiDefinition, id, ct));
    }

    private static async Task<IResult> Revert(
        Guid id,
        int versionNumber,
        ITenantApiDefinitionRepository repo,
        [FromKeyedServices("tenant")] IVersionHistoryService versions,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();

        var snapshotJson = await versions.GetSnapshotAsync(VersionedEntityType.TenantApiDefinition, id, versionNumber, ct);
        if (snapshotJson is null) return Results.NotFound(new { error = $"Version {versionNumber} not found." });

        var snapshot = JsonSerializer.Deserialize<ApiDefinitionSnapshot>(snapshotJson)
            ?? throw new InvalidOperationException("Stored API definition snapshot is corrupt.");
        ApplySnapshot(def, snapshot);

        await repo.SaveChangesAsync(ct);
        await versions.SnapshotAsync(
            VersionedEntityType.TenantApiDefinition, def.Id, BuildSnapshot(def),
            actor.Value.Id, actor.Value.Name, $"Reverted to version {versionNumber}", ct);
        return Results.Ok(ToResponse(def));
    }

    private static string BuildSnapshot(TenantApiDefinition d) => JsonSerializer.Serialize(new ApiDefinitionSnapshot(
        d.ApiCategory, d.Provider, d.Name, d.Description, d.HttpMethod, d.BaseUrl, d.TimeoutSeconds,
        d.Headers, d.QueryParams, d.RequestBodyTemplate, d.ResponseMapping, d.AuthConfig, d.IsActive));

    private static void ApplySnapshot(TenantApiDefinition d, ApiDefinitionSnapshot s)
    {
        if (d.ApiCategory != s.ApiCategory) d.UpdateCategory(s.ApiCategory);
        d.Update(s.Name, s.HttpMethod, s.BaseUrl, s.Description, s.Provider ?? "", s.TimeoutSeconds);
        d.SetHeaders(s.Headers);
        d.SetQueryParams(s.QueryParams);
        d.SetRequestBodyTemplate(s.RequestBodyTemplate);
        d.SetResponseMapping(s.ResponseMapping);
        d.SetAuthConfig(s.AuthConfig);
        if (s.IsActive) d.Activate(); else d.Deactivate();
    }

    private static async Task<IResult> Activate(Guid id, ITenantApiDefinitionRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();
        def.Activate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Deactivate(Guid id, ITenantApiDefinitionRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();
        def.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Delete(Guid id, ITenantApiDefinitionRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();
        await repo.DeleteAsync(def, ct);
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAuth(
        TestAuthRequest request,
        ITenantCredentialStore credStore,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
        => await AuthTestHelper.RunAuthTest(request.AuthConfig, credStore.GetAsync, httpFactory, ct);

    private static object ToResponse(TenantApiDefinition d) => new
    {
        d.Id,
        d.ApiCategory,
        d.Provider,
        d.Name,
        d.Description,
        d.HttpMethod,
        d.BaseUrl,
        d.TimeoutSeconds,
        d.Headers,
        d.QueryParams,
        d.RequestBodyTemplate,
        d.ResponseMapping,
        d.AuthConfig,
        d.IsActive,
        d.CreatedAt,
        d.UpdatedAt,
    };
}

// Shared request records for both portal and tenant endpoints
public record CreateApiDefinitionRequest(
    string ApiCategory,
    string Name,
    string HttpMethod,
    string BaseUrl,
    string? Description,
    string? Provider,
    int? TimeoutSeconds,
    string? AuthConfig);

public record UpdateApiDefinitionRequest(
    string Name,
    string HttpMethod,
    string BaseUrl,
    string? ApiCategory,
    string? Description,
    string? Provider,
    int? TimeoutSeconds,
    string? Headers,
    string? QueryParams,
    string? RequestBodyTemplate,
    string? ResponseMapping,
    string? AuthConfig);
