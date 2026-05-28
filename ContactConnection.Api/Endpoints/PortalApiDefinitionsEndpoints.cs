using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class PortalApiDefinitionsEndpoints
{
    public static IEndpointRouteBuilder MapPortalApiDefinitionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/api-definitions")
            .RequireAuthorization("PlatformAdmin");

        group.MapGet("", GetAll);
        group.MapPost("", Create);
        group.MapGet("{id:guid}", GetById);
        group.MapPut("{id:guid}", Update);
        group.MapPost("{id:guid}/activate", Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);
        group.MapDelete("{id:guid}", Delete);

        return app;
    }

    private static async Task<IResult> GetAll(
        IPortalApiDefinitionRepository repo,
        CancellationToken ct)
    {
        var all = await repo.GetAllAsync(ct);
        return Results.Ok(all.Select(ToResponse));
    }

    private static async Task<IResult> GetById(
        Guid id,
        IPortalApiDefinitionRepository repo,
        CancellationToken ct)
    {
        var def = await repo.GetByIdAsync(id, ct);
        return def is null ? Results.NotFound() : Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Create(
        CreateApiDefinitionRequest request,
        IPortalApiDefinitionRepository repo,
        CancellationToken ct)
    {
        if (!ApiDefinitionType.IsValid(request.ApiType))
            return Results.BadRequest(new { error = $"Unknown api_type '{request.ApiType}'. Valid types: {string.Join(", ", ApiDefinitionType.All)}" });

        var def = PortalApiDefinition.Create(request.ApiType, request.Name, request.HttpMethod, request.BaseUrl, request.Description, request.Provider, request.TimeoutSeconds ?? 30);
        await repo.AddAsync(def, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/portal/api-definitions/{def.Id}", ToResponse(def));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateApiDefinitionRequest request,
        IPortalApiDefinitionRepository repo,
        CancellationToken ct)
    {
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();

        def.Update(request.Name, request.HttpMethod, request.BaseUrl, request.Description, request.Provider, request.TimeoutSeconds);
        if (request.Headers is not null) def.SetHeaders(request.Headers);
        if (request.QueryParams is not null) def.SetQueryParams(request.QueryParams);
        if (request.RequestBodyTemplate is not null) def.SetRequestBodyTemplate(request.RequestBodyTemplate);
        if (request.ResponseMapping is not null) def.SetResponseMapping(request.ResponseMapping);
        if (request.AuthConfig is not null) def.SetAuthConfig(request.AuthConfig);

        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Activate(Guid id, IPortalApiDefinitionRepository repo, CancellationToken ct)
    {
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();
        def.Activate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Deactivate(Guid id, IPortalApiDefinitionRepository repo, CancellationToken ct)
    {
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();
        def.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(def));
    }

    private static async Task<IResult> Delete(Guid id, IPortalApiDefinitionRepository repo, CancellationToken ct)
    {
        var def = await repo.GetByIdAsync(id, ct);
        if (def is null) return Results.NotFound();
        await repo.DeleteAsync(def, ct);
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static object ToResponse(PortalApiDefinition d) => new
    {
        d.Id,
        d.ApiType,
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
