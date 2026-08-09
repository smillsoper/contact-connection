using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class AdminApiPreferencesEndpoints
{
    public static IEndpointRouteBuilder MapAdminApiPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        var prefsGroup = app.MapGroup("/api/v1/admin/api-preferences")
            .RequireAuthorization("TenantAdmin");

        prefsGroup.MapGet("", GetAll);
        prefsGroup.MapPut("{subType}", Upsert);
        prefsGroup.MapDelete("{subType}", Delete);

        // Lists all available portal + tenant endpoints for a given sub-type
        var availGroup = app.MapGroup("/api/v1/admin/available-endpoints")
            .RequireAuthorization("TenantAdmin");

        availGroup.MapGet("{subType}", GetAvailable);

        return app;
    }

    private static async Task<IResult> GetAll(
        ITenantApiPreferenceRepository prefRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var prefs = await prefRepo.GetAllAsync(ct);
        return Results.Ok(prefs.Select(ToPrefResponse));
    }

    private static async Task<IResult> Upsert(
        string subType,
        SetTenantPreferenceRequest request,
        ITenantApiPreferenceRepository prefRepo,
        IPortalApiEndpointRepository portalEndpointRepo,
        ITenantApiEndpointRepository tenantEndpointRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        if (!ApiSubType.IsValid(subType))
            return Results.BadRequest(new { error = $"Unknown api_sub_type '{subType}'." });

        if (!ApiPreferenceSource.IsValid(request.Source))
            return Results.BadRequest(new { error = $"Unknown source '{request.Source}'. Valid values: {ApiPreferenceSource.Portal}, {ApiPreferenceSource.Tenant}." });

        // Verify the referenced endpoint exists and matches the sub-type — whichever table
        // Source points at.
        string? actualSubType = request.Source == ApiPreferenceSource.Portal
            ? (await portalEndpointRepo.GetByIdAsync(request.EndpointId, ct))?.ApiSubType
            : (await tenantEndpointRepo.GetByIdAsync(request.EndpointId, ct))?.ApiSubType;

        if (actualSubType is null)
            return Results.NotFound(new { error = $"{request.Source} endpoint not found." });
        if (actualSubType != subType)
            return Results.BadRequest(new { error = $"Endpoint sub-type '{actualSubType}' does not match '{subType}'." });

        await prefRepo.UpsertAsync(subType, request.Source, request.EndpointId, request.SettingsJson, ct);
        await prefRepo.SaveChangesAsync(ct);

        var saved = await prefRepo.GetBySubTypeAsync(subType, ct);
        return Results.Ok(ToPrefResponse(saved!));
    }

    private static async Task<IResult> Delete(
        string subType,
        ITenantApiPreferenceRepository prefRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        await prefRepo.DeleteBySubTypeAsync(subType, ct);
        await prefRepo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAvailable(
        string subType,
        IPortalApiDefinitionRepository portalDefRepo,
        IPortalApiEndpointRepository portalEndpointRepo,
        ITenantApiDefinitionRepository tenantDefRepo,
        ITenantApiEndpointRepository tenantEndpointRepo,
        ITenantApiPreferenceRepository prefRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        if (!ApiSubType.IsValid(subType))
            return Results.BadRequest(new { error = $"Unknown api_sub_type '{subType}'." });

        var tenantPref = await prefRepo.GetBySubTypeAsync(subType, ct);

        // Portal endpoints for this sub-type — platform catalog, read-only for the tenant.
        var portalEndpoints = await portalEndpointRepo.GetBySubTypeAsync(subType, ct);
        var portalItems = new List<object>();
        foreach (var e in portalEndpoints)
        {
            var def = await portalDefRepo.GetByIdAsync(e.DefinitionId, ct);
            portalItems.Add(new
            {
                source = ApiPreferenceSource.Portal,
                definitionId = e.DefinitionId,
                definitionName = def?.Name,
                definitionProvider = def?.Provider,
                e.Id,
                e.ApiSubType,
                e.Name,
                e.Path,
                e.IsPreferred,
                e.IsActive,
                isTenantSelected = tenantPref?.Source == ApiPreferenceSource.Portal && tenantPref.EndpointId == e.Id,
            });
        }

        // Tenant-owned endpoints for this sub-type — the tenant's own registration, e.g. their
        // own paid subscription to a vendor rather than sharing the platform's.
        var tenantEndpoints = await tenantEndpointRepo.GetBySubTypeAsync(subType, ct);
        var tenantItems = new List<object>();
        foreach (var e in tenantEndpoints)
        {
            var def = await tenantDefRepo.GetByIdAsync(e.DefinitionId, ct);
            tenantItems.Add(new
            {
                source = ApiPreferenceSource.Tenant,
                definitionId = e.DefinitionId,
                definitionName = def?.Name,
                definitionProvider = def?.Provider,
                e.Id,
                e.ApiSubType,
                e.Name,
                e.Path,
                e.IsPreferred,
                e.IsActive,
                isTenantSelected = tenantPref?.Source == ApiPreferenceSource.Tenant && tenantPref.EndpointId == e.Id,
            });
        }

        return Results.Ok(new
        {
            subType,
            tenantPreference = tenantPref is null ? null : new { tenantPref.Source, tenantPref.EndpointId, tenantPref.SettingsJson },
            portalEndpoints = portalItems,
            tenantEndpoints = tenantItems,
        });
    }

    private static object ToPrefResponse(TenantApiPreference p) => new
    {
        p.Id,
        p.ApiSubType,
        p.Source,
        p.EndpointId,
        p.SettingsJson,
        p.CreatedAt,
        p.UpdatedAt,
    };
}

public record SetTenantPreferenceRequest(string Source, Guid EndpointId, string? SettingsJson = null);
