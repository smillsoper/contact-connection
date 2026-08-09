using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Exposes the live-registered ITtsStreamProvider keys, each with its RequiredCredentialFields,
/// so both the Portal admin UI (picking a valid PortalApiDefinition.Provider — see
/// TtsProviderValidation) and the tenant admin UI (building a labeled credential-entry form
/// instead of asking someone to know a raw key name like "apiKey" exists) can stay in sync with
/// whatever's actually registered in DI, never a hand-maintained list on either side.
/// </summary>
public static class TtsProvidersEndpoints
{
    public static IEndpointRouteBuilder MapPortalTtsProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/portal/tts-providers", GetAll)
            .RequireAuthorization("PlatformAdmin");
        return app;
    }

    public static IEndpointRouteBuilder MapAdminTtsProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/tts-providers", GetAll)
            .RequireAuthorization("TenantAdmin");
        return app;
    }

    private static IResult GetAll(ITtsStreamProviderFactory factory)
    {
        var result = factory.RegisteredProviderKeys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k =>
            {
                var provider = factory.Resolve(k);
                return new
                {
                    key = provider.ProviderKey,
                    requiredCredentialFields = provider.RequiredCredentialFields,
                };
            });
        return Results.Ok(result);
    }
}
