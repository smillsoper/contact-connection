using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class DashboardsEndpoints
{
    public static void MapDashboardsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboards").RequireAuthorization();

        group.MapPost("/", async (
            CreateDashboardRequest req,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var agentIdClaim = http.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(agentIdClaim, out var agentId))
                return Results.Unauthorized();

            var dashboard = Dashboard.Create(
                tenantId:         tenantContext.Current.Id,
                createdByAgentId: agentId,
                name:             req.Name,
                isShared:         req.IsShared,
                layout:           req.Layout);

            await dashboards.AddAsync(dashboard, ct);
            await dashboards.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/dashboards/{dashboard.Id}", dashboard.ToResponse());
        });

        group.MapGet("/", async (
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var agentIdClaim = http.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(agentIdClaim, out var agentId))
                return Results.Unauthorized();

            var list = await dashboards.GetVisibleAsync(tenantContext.Current.Id, agentId, ct);
            return Results.Ok(list.Select(d => d.ToResponse()));
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            return Results.Ok(dashboard.ToDetailResponse());
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDashboardRequest req,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            dashboard.Update(req.Name, req.IsShared, req.Layout);
            await dashboards.SaveChangesAsync(ct);

            return Results.Ok(dashboard.ToDetailResponse());
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            dashboards.Delete(dashboard);
            await dashboards.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    private static object ToResponse(this Dashboard d) => new
    {
        id                  = d.Id,
        name                = d.Name,
        is_shared           = d.IsShared,
        created_by_agent_id = d.CreatedByAgentId,
        created_at          = d.CreatedAt,
        updated_at          = d.UpdatedAt,
    };

    private static object ToDetailResponse(this Dashboard d) => new
    {
        id                  = d.Id,
        name                = d.Name,
        is_shared           = d.IsShared,
        created_by_agent_id = d.CreatedByAgentId,
        created_at          = d.CreatedAt,
        updated_at          = d.UpdatedAt,
        layout              = d.Layout,
    };
}

public record CreateDashboardRequest(string Name, bool IsShared, string Layout);
public record UpdateDashboardRequest(string Name, bool IsShared, string Layout);
