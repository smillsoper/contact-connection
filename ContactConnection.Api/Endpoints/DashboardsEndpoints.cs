using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

public static class DashboardsEndpoints
{
    public static void MapDashboardsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboards");

        group.MapPost("/", async (
            CreateDashboardRequest req,
            IDashboardRepository dashboards,
            ScopedTenantDbContextFactory dbf,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();
            if (await ValidateClientScopeAsync(dbf, req.IsClientDashboard, req.ScopeClientId, req.ScopeCampaignIds, req.Layout, ct) is { } bad)
                return Results.BadRequest(new { error = bad });

            var dashboard = Dashboard.Create(
                tenantId:         tenantContext.Current.Id,
                createdByAgentId: agentId,
                name:             req.Name,
                isShared:         req.IsShared,
                layout:           req.Layout);
            dashboard.SetClientScope(req.IsClientDashboard, req.ScopeClientId, req.ScopeCampaignIds);

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
            IAuthorizationService auth,
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

            var canEdit = (await auth.AuthorizeAsync(http.User, "ReportsManage")).Succeeded
                          && await CanEditAsync(http, auth, dashboard, agentId);
            return Results.Ok(dashboard.ToDetailResponse(canEdit));
        }).RequireAuthorization("ReportsView");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDashboardRequest req,
            IDashboardRepository dashboards,
            ScopedTenantDbContextFactory dbf,
            TenantContext tenantContext,
            HttpContext http,
            IAuthorizationService auth,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id || !dashboard.IsVisibleTo(agentId))
                return Results.NotFound();
            // Sharing controls visibility, not editability — a shared dashboard is editable only by
            // the agent who created it or a tenant admin (a team dashboard an admin can maintain,
            // S163). reports.manage alone (the route policy below) doesn't grant editing someone
            // else's dashboard; such users save their own copy instead.
            if (!await CanEditAsync(http, auth, dashboard, agentId)) return Results.Forbid();
            if (await ValidateClientScopeAsync(dbf, req.IsClientDashboard, req.ScopeClientId, req.ScopeCampaignIds, req.Layout, ct) is { } bad)
                return Results.BadRequest(new { error = bad });

            dashboard.Update(req.Name, req.IsShared, req.Layout);
            dashboard.SetClientScope(req.IsClientDashboard, req.ScopeClientId, req.ScopeCampaignIds);
            await dashboards.SaveChangesAsync(ct);

            return Results.Ok(dashboard.ToDetailResponse(canEdit: true));
        }).RequireAuthorization("ReportsManage");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDashboardRepository dashboards,
            TenantContext tenantContext,
            HttpContext http,
            IAuthorizationService auth,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();

            var dashboard = await dashboards.GetByIdAsync(id, ct);
            if (dashboard is null || dashboard.TenantId != tenantContext.Current.Id || !dashboard.IsVisibleTo(agentId))
                return Results.NotFound();
            if (!await CanEditAsync(http, auth, dashboard, agentId)) return Results.Forbid();

            dashboards.Delete(dashboard);
            await dashboards.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization("ReportsManage");
    }

    /// <summary>
    /// A client dashboard (S181) needs a client, campaigns that belong to it, and only report-type widgets — the client
    /// portal also enforces all of this when it serves data, this just stops a misconfigured one being saved.
    /// </summary>
    private static async Task<string?> ValidateClientScopeAsync(ScopedTenantDbContextFactory dbf, bool isClient, Guid? clientId,
        List<Guid>? campaignIds, string layout, CancellationToken ct)
    {
        if (!isClient) return null;
        if (clientId is null) return "Choose the client this dashboard is for.";
        await using var db = dbf.Create();
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct)) return "That client doesn't exist.";
        if (campaignIds is { Count: > 0 })
        {
            var ids = campaignIds.Distinct().ToList();
            var owned = await db.Campaigns.CountAsync(c => ids.Contains(c.Id) && c.ClientId == clientId, ct);
            if (owned != ids.Count) return "Every campaign must belong to the chosen client.";
        }
        try
        {
            var widgets = System.Text.Json.Nodes.JsonNode.Parse(layout) as System.Text.Json.Nodes.JsonArray ?? [];
            var bad = widgets.Select(w => w?["widgetType"]?.GetValue<string>()).Where(t => t is null || !Dashboard.ClientWidgetTypes.Contains(t)).Distinct().ToList();
            if (bad.Count > 0) return $"A client dashboard can only hold report widgets (KPIs, Service Level, Call State by Campaign, Call Records). Remove: {string.Join(", ", bad)}.";
        }
        catch (System.Text.Json.JsonException) { return "The layout isn't valid."; }
        return null;
    }

    /// <summary>Owner, or a tenant admin (same "TenantAdmin" policy as the rest of the admin API).</summary>
    private static async Task<bool> CanEditAsync(HttpContext http, IAuthorizationService auth, Dashboard d, Guid agentId) =>
        d.CreatedByAgentId == agentId || (await auth.AuthorizeAsync(http.User, "TenantAdmin")).Succeeded;

    private static bool TryGetAgentId(HttpContext http, out Guid agentId) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out agentId);

    private static object ToResponse(this Dashboard d) => new
    {
        id                  = d.Id,
        name                = d.Name,
        is_shared           = d.IsShared,
        is_client_dashboard = d.IsClientDashboard,
        scope_client_id     = d.ScopeClientId,
        scope_campaign_ids  = d.ScopeCampaignIds,
        created_by_agent_id = d.CreatedByAgentId,
        created_at          = d.CreatedAt,
        updated_at          = d.UpdatedAt,
    };

    private static object ToDetailResponse(this Dashboard d, bool canEdit) => new
    {
        can_edit            = canEdit,
        id                  = d.Id,
        name                = d.Name,
        is_shared           = d.IsShared,
        is_client_dashboard = d.IsClientDashboard,
        scope_client_id     = d.ScopeClientId,
        scope_campaign_ids  = d.ScopeCampaignIds,
        created_by_agent_id = d.CreatedByAgentId,
        created_at          = d.CreatedAt,
        updated_at          = d.UpdatedAt,
        layout              = d.Layout,
    };
}

/// <param name="IsClientDashboard">S181: shown to client users, locked to <paramref name="ScopeClientId"/> (and optionally some of its campaigns).</param>
public record CreateDashboardRequest(string Name, bool IsShared, string Layout, bool IsClientDashboard = false, Guid? ScopeClientId = null, List<Guid>? ScopeCampaignIds = null);
public record UpdateDashboardRequest(string Name, bool IsShared, string Layout, bool IsClientDashboard = false, Guid? ScopeClientId = null, List<Guid>? ScopeCampaignIds = null);
