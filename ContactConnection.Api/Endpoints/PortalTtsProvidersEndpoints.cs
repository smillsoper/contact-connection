using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Exposes the live-registered ITtsStreamProvider keys so the Portal admin UI can offer a real
/// picker for PortalApiDefinition.Provider under ApiSubType.TtsStreaming, instead of free text
/// that has to exactly match a string hardcoded in Infrastructure code with no UI signal that
/// it matters. Reflects whatever's actually registered in DI — never drifts out of sync the way
/// a hand-maintained frontend list would.
/// </summary>
public static class PortalTtsProvidersEndpoints
{
    public static IEndpointRouteBuilder MapPortalTtsProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/portal/tts-providers", GetAll)
            .RequireAuthorization("PlatformAdmin");
        return app;
    }

    private static IResult GetAll(ITtsStreamProviderFactory factory) =>
        Results.Ok(factory.RegisteredProviderKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
}
