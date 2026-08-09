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
