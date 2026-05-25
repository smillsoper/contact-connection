using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class TenantsEndpoints
{
    public static IEndpointRouteBuilder MapTenantsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenants").RequireAuthorization();

        group.MapGet("me", GetCurrentTenant);
        group.MapGet("{id:guid}", GetById);
        group.MapPost("", Provision);

        return app;
    }

    private static IResult GetCurrentTenant(TenantContext tenantContext)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var t = tenantContext.Current;
        return Results.Ok(new
        {
            t.Id,
            t.Name,
            t.DisplayName,
            t.LogoUrl,
            t.Subdomain,
            t.Timezone,
            t.Settings,
            t.OnboardingComplete,
        });
    }

    private static async Task<IResult> GetById(
        Guid id,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        return tenant is null ? Results.NotFound() : Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> Provision(
        ProvisionTenantRequest request,
        ITenantProvisioningService provisioning,
        CancellationToken ct)
    {
        try
        {
            var (tenant, _) = await provisioning.ProvisionAsync(
                request.Name,
                request.Subdomain,
                request.Timezone,
                ct: ct);

            return Results.Created($"/api/v1/tenants/{tenant.Id}", ToResponse(tenant));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static object ToResponse(Tenant t) => new
    {
        t.Id,
        t.Name,
        t.Subdomain,
        t.CustomDomain,
        t.SchemaName,
        t.PlanTier,
        t.Timezone,
        t.IsActive,
        t.TrialExpiresAt,
        t.BillingContact,
        t.FeatureFlags,
        t.CreatedAt
    };
}

public record ProvisionTenantRequest(
    string Name,
    string Subdomain,
    string Timezone);
