using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

public static class PortalMaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapPortalMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/maintenance")
            .RequireAuthorization("PlatformAdmin");

        group.MapPost("migrate-tenants", MigrateTenants);

        return app;
    }

    private static async Task<IResult> MigrateTenants(
        ITenantProvisioningService provisioning,
        CancellationToken ct)
    {
        var result = await provisioning.MigrateAllTenantsAsync(ct);

        return result.Errors.Count == 0
            ? Results.Ok(new { result.Migrated, result.Errors })
            : Results.Json(new { result.Migrated, result.Errors }, statusCode: 207);
    }
}
