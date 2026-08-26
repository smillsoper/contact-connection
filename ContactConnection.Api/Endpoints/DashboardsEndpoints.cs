using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class DashboardsEndpoints
{
    public static void MapDashboardsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboards");

        group.MapPost("/", async (
            CreateDashboardRequest req,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var dashboard = Dashboard.Create(
                tenantId:         tenantContext.Current.Id,
                createdByAgentId: agentId,
                name:             req.Name,
                isShared:         req.IsShared,
                layout:           req.Layout);

            await dashboards.AddAsync(dashboard, ct);
            await dashboards.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/dashboards/{dashboard.Id}", dashboard.ToResponse());
        }).RequireAuthorization("ReportsManage");

        group.MapGet("/", async (
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var list = await dashboards.GetVisibleAsync(tenantContext.Current.Id, agentId, ct);
            return Results.Ok(list.Select(d => d.ToResponse()));
        }).RequireAuthorization("ReportsView");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            // A private (unshared) dashboard belonging to someone else is treated exactly like a
            // dashboard that doesn't exist — same as GetVisibleAsync already does for the list —
            // so its existence isn't leaked to a tenant-mate who merely also holds reports.view.
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id || !dashboard.IsVisibleTo(agentId))
                return Results.NotFound();

            return Results.Ok(dashboard.ToDetailResponse());
        }).RequireAuthorization("ReportsView");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDashboardRequest req,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id || !dashboard.IsVisibleTo(agentId))
                return Results.NotFound();
            // Sharing controls visibility, not editability — a shared dashboard is read-only to
            // everyone except the agent who created it. reports.manage alone (checked by the
            // route policy below) is not the same as owning this specific dashboard.
            if (dashboard.CreatedByAgentId != agentId) return Results.Forbid();

            dashboard.Update(req.Name, req.IsShared, req.Layout);
            await dashboards.SaveChangesAsync(ct);

            return Results.Ok(dashboard.ToDetailResponse());
        }).RequireAuthorization("ReportsManage");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id || !dashboard.IsVisibleTo(agentId))
                return Results.NotFound();
            if (dashboard.CreatedByAgentId != agentId) return Results.Forbid();

            dashboards.Delete(dashboard);
            await dashboards.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization("ReportsManage");
    }

    private static bool TryGetAgentId(HttpContext http, out Guid agentId) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out agentId);

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
