using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class PortalApiEndpointsEndpoints
{
    public static IEndpointRouteBuilder MapPortalApiEndpointsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/api-definitions/{definitionId:guid}/endpoints")
            .RequireAuthorization("PlatformAdmin");

        group.MapGet("", GetAll);
        group.MapPost("", Create);
        group.MapPost("test", TestEndpoint);
        group.MapGet("{endpointId:guid}", GetById);
        group.MapPut("{endpointId:guid}", Update);
        group.MapPost("{endpointId:guid}/set-preferred", SetPreferred);
        group.MapDelete("{endpointId:guid}", Delete);

        return app;
    }

    private static async Task<IResult> GetAll(
        Guid definitionId,
        IPortalApiDefinitionRepository defRepo,
        IPortalApiEndpointRepository repo,
        CancellationToken ct)
    {
        var def = await defRepo.GetByIdAsync(definitionId, ct);
        if (def is null) return Results.NotFound();

        var endpoints = await repo.GetByDefinitionAsync(definitionId, ct);
        return Results.Ok(endpoints.Select(ToResponse));
    }

    private static async Task<IResult> GetById(
        Guid definitionId,
        Guid endpointId,
        IPortalApiEndpointRepository repo,
        CancellationToken ct)
    {
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> Create(
        Guid definitionId,
        CreateApiEndpointRequest request,
        IPortalApiDefinitionRepository defRepo,
        IPortalApiEndpointRepository repo,
        CancellationToken ct)
    {
        var def = await defRepo.GetByIdAsync(definitionId, ct);
        if (def is null) return Results.NotFound();

        if (!ApiSubType.IsValid(request.ApiSubType))
            return Results.BadRequest(new { error = $"Unknown api_sub_type '{request.ApiSubType}'. Valid sub-types: {string.Join(", ", ApiSubType.All)}" });

        var endpoint = PortalApiEndpoint.Create(
            definitionId,
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

        await repo.AddAsync(endpoint, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/portal/api-definitions/{definitionId}/endpoints/{endpoint.Id}", ToResponse(endpoint));
    }

    private static async Task<IResult> Update(
        Guid definitionId,
        Guid endpointId,
        UpdateApiEndpointRequest request,
        IPortalApiEndpointRepository repo,
        CancellationToken ct)
    {
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        if (request.ApiSubType is not null)
        {
            if (!ApiSubType.IsValid(request.ApiSubType))
                return Results.BadRequest(new { error = $"Unknown api_sub_type '{request.ApiSubType}'." });
            endpoint.UpdateSubType(request.ApiSubType);
        }
        endpoint.Update(request.Name, request.Path, request.HttpMethod, request.Description, request.SortOrder);
        if (request.RequestBodyTemplate is not null) endpoint.SetRequestBodyTemplate(request.RequestBodyTemplate);
        if (request.QueryParams is not null) endpoint.SetQueryParams(request.QueryParams);
        if (request.Headers is not null) endpoint.SetHeaders(request.Headers);
        if (request.ResponseMapping is not null) endpoint.SetResponseMapping(request.ResponseMapping);

        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> SetPreferred(
        Guid definitionId,
        Guid endpointId,
        IPortalApiEndpointRepository repo,
        CancellationToken ct)
    {
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();

        // Clear current preferred for this sub-type, then set the new one
        await repo.ClearPreferredForSubTypeAsync(endpoint.ApiSubType, ct);
        endpoint.SetPreferred();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> TestEndpoint(
        Guid definitionId,
        RunEndpointTestRequest request,
        IPortalApiDefinitionRepository defRepo,
        IPortalCredentialStore credStore,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
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
        IPortalApiEndpointRepository repo,
        CancellationToken ct)
    {
        var endpoint = await repo.GetByIdAsync(endpointId, ct);
        if (endpoint is null || endpoint.DefinitionId != definitionId) return Results.NotFound();
        await repo.DeleteAsync(endpoint, ct);
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static object ToResponse(PortalApiEndpoint e) => new
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
        e.CreatedAt,
        e.UpdatedAt,
    };
}

public record CreateApiEndpointRequest(
    string ApiSubType,
    string Name,
    string Path,
    string? HttpMethod,
    string? Description,
    int? SortOrder,
    string? RequestBodyTemplate,
    string? QueryParams,
    string? Headers,
    string? ResponseMapping);

public record UpdateApiEndpointRequest(
    string Name,
    string Path,
    string? ApiSubType,
    string? HttpMethod,
    string? Description,
    int? SortOrder,
    string? RequestBodyTemplate,
    string? QueryParams,
    string? Headers,
    string? ResponseMapping);
