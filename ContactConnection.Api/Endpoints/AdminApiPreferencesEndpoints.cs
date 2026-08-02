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
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        if (!ApiSubType.IsValid(subType))
            return Results.BadRequest(new { error = $"Unknown api_sub_type '{subType}'." });

        // Verify the portal endpoint exists and matches the sub-type
        var portalEndpoint = await portalEndpointRepo.GetByIdAsync(request.PortalApiEndpointId, ct);
        if (portalEndpoint is null)
            return Results.NotFound(new { error = "Portal endpoint not found." });
        if (portalEndpoint.ApiSubType != subType)
            return Results.BadRequest(new { error = $"Portal endpoint sub-type '{portalEndpoint.ApiSubType}' does not match '{subType}'." });

        await prefRepo.UpsertAsync(subType, request.PortalApiEndpointId, request.SettingsJson, ct);
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

        // Portal endpoints for this sub-type
        var portalEndpoints = await portalEndpointRepo.GetBySubTypeAsync(subType, ct);
        var portalItems = new List<object>();
        foreach (var e in portalEndpoints)
        {
            var def = await portalDefRepo.GetByIdAsync(e.DefinitionId, ct);
            portalItems.Add(new
            {
                source = "portal",
                definitionId = e.DefinitionId,
                definitionName = def?.Name,
                definitionProvider = def?.Provider,
                e.Id,
                e.ApiSubType,
                e.Name,
                e.Path,
                e.IsPreferred,
                e.IsActive,
                isTenantSelected = tenantPref?.PortalApiEndpointId == e.Id,
            });
        }

        // Tenant-owned endpoints for this sub-type
        var tenantEndpoints = await tenantEndpointRepo.GetBySubTypeAsync(subType, ct);
        var tenantItems = new List<object>();
        foreach (var e in tenantEndpoints)
        {
            var def = await tenantDefRepo.GetByIdAsync(e.DefinitionId, ct);
            tenantItems.Add(new
            {
                source = "tenant",
                definitionId = e.DefinitionId,
                definitionName = def?.Name,
                definitionProvider = def?.Provider,
                e.Id,
                e.ApiSubType,
                e.Name,
                e.Path,
                e.IsPreferred,
                e.IsActive,
            });
        }

        return Results.Ok(new
        {
            subType,
            tenantPreference = tenantPref is null ? null : new { tenantPref.PortalApiEndpointId, tenantPref.SettingsJson },
            portalEndpoints = portalItems,
            tenantEndpoints = tenantItems,
        });
    }

    private static object ToPrefResponse(TenantApiPreference p) => new
    {
        p.Id,
        p.ApiSubType,
        p.PortalApiEndpointId,
        p.SettingsJson,
        p.CreatedAt,
        p.UpdatedAt,
    };
}

public record SetTenantPreferenceRequest(Guid PortalApiEndpointId, string? SettingsJson = null);
