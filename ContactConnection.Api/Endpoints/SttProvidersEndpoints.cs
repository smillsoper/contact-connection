using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Recognition mirror of TtsProvidersEndpoints (S148) — exposes the live-registered
/// ISpeechRecognitionProvider keys, each with its RequiredCredentialFields, so both the Portal
/// admin UI (picking a valid PortalApiDefinition.Provider — see SttProviderValidation) and the
/// tenant admin UI can stay in sync with whatever's actually registered in DI.
/// </summary>
public static class SttProvidersEndpoints
{
    public static IEndpointRouteBuilder MapPortalSttProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/portal/stt-providers", GetAll)
            .RequireAuthorization("PlatformAdmin");
        return app;
    }

    public static IEndpointRouteBuilder MapAdminSttProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/stt-providers", GetAll)
            .RequireAuthorization("TenantAdmin");
        return app;
    }

    private static IResult GetAll(ISpeechRecognitionProviderFactory factory)
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
