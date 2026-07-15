using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
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
        group.MapPost("test-auth", TestAuth);
        group.MapGet("{id:guid}", GetById);
        group.MapPut("{id:guid}", Update);
        group.MapPost("{id:guid}/activate", Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);
        group.MapDelete("{id:guid}", Delete);

        return app;
    }

    private static async Task<IResult> GetAll(
        IPortalApiDefinitionRepository repo,
        string? category,
        CancellationToken ct)
    {
        var all = category is null
            ? await repo.GetAllAsync(ct)
            : await repo.GetByCategoryAsync(category, ct);
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
        if (!ApiCategory.IsValid(request.ApiCategory))
            return Results.BadRequest(new { error = $"Unknown api_category '{request.ApiCategory}'. Valid categories: {string.Join(", ", ApiCategory.All)}" });

        var def = PortalApiDefinition.Create(request.ApiCategory, request.Name, request.HttpMethod, request.BaseUrl, request.Description, request.Provider, request.TimeoutSeconds ?? 30);
        if (request.AuthConfig is not null) def.SetAuthConfig(request.AuthConfig);
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

        if (request.ApiCategory is not null)
        {
            if (!ApiCategory.IsValid(request.ApiCategory))
                return Results.BadRequest(new { error = $"Unknown api_category '{request.ApiCategory}'." });
            def.UpdateCategory(request.ApiCategory);
        }
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

    private static async Task<IResult> TestAuth(
        TestAuthRequest request,
        IPortalCredentialStore credStore,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
        => await AuthTestHelper.RunAuthTest(request.AuthConfig, credStore.GetAsync, httpFactory, ct);

    private static object ToResponse(PortalApiDefinition d) => new
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
