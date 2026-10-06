using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantRepository tenants, TenantContext tenantContext)
    {
        // Nginx forwards the subdomain in X-Tenant-Subdomain.
        // Fall back to parsing the host header directly for local dev without nginx.
        var subdomain = context.Request.Headers["X-Tenant-Subdomain"].FirstOrDefault()
            ?? ExtractSubdomain(context.Request.Host.Host);

        if (!string.IsNullOrEmpty(subdomain))
        {
            var tenant = await tenants.GetBySubdomainAsync(subdomain);
            if (tenant is not null)
            {
                // A signed-in user's token belongs to one tenant — never let the header switch them into another (S181).
                if (TokenTenantMismatch(context.User, tenant.Id))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = "This sign-in belongs to a different organization." });
                    return;
                }
                context.Items["Tenant"] = tenant;
                tenantContext.Current = tenant;  // Feeds TenantDbContextFactory
            }
        }

        await _next(context);
    }

    /// <summary>True when the request carries a tenant token (agent or client user) for a different tenant than the one
    /// resolved from the header / host. Portal (platform) tokens carry no tenant claim and are unaffected.</summary>
    public static bool TokenTenantMismatch(System.Security.Claims.ClaimsPrincipal user, Guid tenantId)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        var claim = user.FindFirst("tenant_id")?.Value;
        return claim is not null && !string.Equals(claim, tenantId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractSubdomain(string host)
    {
        // subdomain.contactconnection.local → subdomain
        var parts = host.Split('.');
        return parts.Length >= 3 ? parts[0] : null;
    }
}

public static class TenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
