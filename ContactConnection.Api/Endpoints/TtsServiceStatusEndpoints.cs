using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Tells the telephony/flow designers whether the current tenant has a TTS streaming vendor
/// configured (Admin → API Preferences → tts_streaming) and, if so, its provider key/display
/// name — so the voice picker on tf_play / tf_transfer / tf_voicemail can offer that vendor's
/// voice as an option alongside the built-in flite voices. Any authenticated agent can read this
/// (unlike /api/v1/admin/api-preferences, which is TenantAdmin-only) since the designer isn't
/// admin-gated.
/// </summary>
public static class TtsServiceStatusEndpoints
{
    public static IEndpointRouteBuilder MapTtsServiceStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/telephony/tts-service-status", GetStatus).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetStatus(
        ITenantApiPreferenceRepository prefRepo,
        IPortalApiEndpointRepository portalEndpointRepo,
        IPortalApiDefinitionRepository portalDefRepo,
        ITenantApiEndpointRepository tenantEndpointRepo,
        ITenantApiDefinitionRepository tenantDefRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var pref = await prefRepo.GetBySubTypeAsync(ApiSubType.TtsStreaming, ct);
        if (pref is null) return Results.Ok(new { configured = false });

        string? provider;
        string? name;
        if (pref.Source == ApiPreferenceSource.Portal)
        {
            var endpoint = await portalEndpointRepo.GetByIdAsync(pref.EndpointId, ct);
            var definition = endpoint is null ? null : await portalDefRepo.GetByIdAsync(endpoint.DefinitionId, ct);
            provider = definition?.Provider;
            name = definition?.Name;
        }
        else
        {
            var endpoint = await tenantEndpointRepo.GetByIdAsync(pref.EndpointId, ct);
            var definition = endpoint is null ? null : await tenantDefRepo.GetByIdAsync(endpoint.DefinitionId, ct);
            provider = definition?.Provider;
            name = definition?.Name;
        }

        if (string.IsNullOrWhiteSpace(provider)) return Results.Ok(new { configured = false });

        return Results.Ok(new { configured = true, providerKey = provider, providerName = name });
    }
}
