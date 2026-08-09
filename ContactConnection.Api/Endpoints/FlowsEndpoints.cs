using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace ContactConnection.Api.Endpoints;

public static class FlowsEndpoints
{
    public static void MapFlowsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/flows").RequireAuthorization();

        // Create a new flow (starts as draft — not active until published)
        group.MapPost("/", async (
            CreateFlowRequest req,
            IFlowRepository flows,
            [FromKeyedServices("tenant")] IVersionHistoryService versions,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null)
                return Results.Unauthorized();

            var agentIdClaim = http.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(agentIdClaim, out var agentId))
                return Results.Unauthorized();

            var actor = ActorResolver.Resolve(http.User);
            if (actor is null) return Results.Unauthorized();

            if (!FlowType.IsValid(req.FlowType))
                return Results.BadRequest(new { error = $"Invalid flow_type. Valid values: {string.Join(", ", FlowType.All)}" });

            var flow = Flow.Create(
                tenantId:         tenantContext.Current.Id,
                createdByAgentId: agentId,
                name:             req.Name,
                flowType:         req.FlowType,
                definition:       req.Definition,
                clientId:         req.ClientId,
                campaignId:       req.CampaignId,
                flowDirection:    req.FlowDirection,
                flowSubType:      req.FlowSubType);

            await flows.AddAsync(flow, ct);
            await flows.SaveChangesAsync(ct);
            await versions.SnapshotAsync(
                VersionedEntityType.Flow, flow.Id, BuildFlowSnapshot(flow),
                actor.Value.Id, actor.Value.Name, "Created", ct);

            return Results.Created($"/api/v1/flows/{flow.Id}", flow.ToResponse());
        });

        // Get a flow by ID (includes definition for designer)
        group.MapGet("/{id:guid}", async (
            Guid id,
            IFlowRepository flows,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var flow = await flows.GetByIdAsync(id, ct);
            if (flow is null || flow.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            return Results.Ok(flow.ToDetailResponse());
        });

        // Update flow name and/or definition (bumps version; does not re-publish)
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateFlowRequest req,
            IFlowRepository flows,
            [FromKeyedServices("tenant")] IVersionHistoryService versions,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var actor = ActorResolver.Resolve(http.User);
            if (actor is null) return Results.Unauthorized();

            var flow = await flows.GetByIdAsync(id, ct);
            if (flow is null || flow.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(req.Name))
                flow.Rename(req.Name);
            flow.UpdateDefinition(req.Definition);
            flow.UpdateMetadata(req.FlowDirection, req.FlowSubType);
            await flows.SaveChangesAsync(ct);
            await versions.SnapshotAsync(
                VersionedEntityType.Flow, flow.Id, BuildFlowSnapshot(flow),
                actor.Value.Id, actor.Value.Name, "Updated", ct);

            return Results.Ok(flow.ToDetailResponse());
        });

        // Version history — newest first
        group.MapGet("/{id:guid}/versions", async (
            Guid id,
            IFlowRepository flows,
            [FromKeyedServices("tenant")] IVersionHistoryService versions,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var flow = await flows.GetByIdAsync(id, ct);
            if (flow is null || flow.TenantId != tenantContext.Current.Id) return Results.NotFound();

            var list = await versions.ListVersionsAsync(VersionedEntityType.Flow, id, ct);
            return Results.Ok(list);
        });

        // Revert to an earlier version — applies that version's snapshot to the live flow AND
        // records the revert as a brand new version (never rewinds/discards history; see
        // API_HARDENING_CHECKLIST.md Tier 1).
        group.MapPost("/{id:guid}/versions/{versionNumber:int}/revert", async (
            Guid id,
            int versionNumber,
            IFlowRepository flows,
            [FromKeyedServices("tenant")] IVersionHistoryService versions,
            TenantContext tenantContext,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var actor = ActorResolver.Resolve(http.User);
            if (actor is null) return Results.Unauthorized();

            var flow = await flows.GetByIdAsync(id, ct);
            if (flow is null || flow.TenantId != tenantContext.Current.Id) return Results.NotFound();

            var snapshotJson = await versions.GetSnapshotAsync(VersionedEntityType.Flow, id, versionNumber, ct);
            if (snapshotJson is null) return Results.NotFound(new { error = $"Version {versionNumber} not found." });

            var snapshot = JsonSerializer.Deserialize<FlowSnapshot>(snapshotJson)
                ?? throw new InvalidOperationException("Stored flow snapshot is corrupt.");

            flow.Rename(snapshot.Name);
            flow.UpdateDefinition(snapshot.Definition);
            flow.UpdateMetadata(snapshot.FlowDirection, snapshot.FlowSubType);
            await flows.SaveChangesAsync(ct);
            await versions.SnapshotAsync(
                VersionedEntityType.Flow, flow.Id, BuildFlowSnapshot(flow),
                actor.Value.Id, actor.Value.Name, $"Reverted to version {versionNumber}", ct);

            return Results.Ok(flow.ToDetailResponse());
        });

        // Publish a draft flow (makes it available for sessions)
        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            IFlowRepository flows,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var flow = await flows.GetByIdAsync(id, ct);
            if (flow is null || flow.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            flow.Publish();
            await flows.SaveChangesAsync(ct);

            return Results.Ok(flow.ToResponse());
        });

        // Delete a flow (draft or published)
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IFlowRepository flows,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var flow = await flows.GetByIdAsync(id, ct);
            if (flow is null || flow.TenantId != tenantContext.Current.Id)
                return Results.NotFound();

            flows.Delete(flow);
            await flows.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        // List active flows for the tenant (agent panel — published only)
        group.MapGet("/", async (
            IFlowRepository flows,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var list = await flows.GetActiveByTenantAsync(tenantContext.Current.Id, ct);
            return Results.Ok(list.Select(f => f.ToResponse()));
        });

        // List all flows for the tenant including drafts (management page).
        // Optional ?type=crm|telephony filter.
        group.MapGet("/all", async (
            IFlowRepository flows,
            TenantContext tenantContext,
            string? type,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var list = await flows.GetAllByTenantAsync(tenantContext.Current.Id, ct);
            if (!string.IsNullOrWhiteSpace(type))
                list = list.Where(f => f.FlowType == type).ToList();
            return Results.Ok(list.Select(f => f.ToResponse()));
        });

        // Endpoints of "general"-category API Definitions available to this tenant's flow
        // designers — merges the tenant's own endpoints with platform-provided ones. Any
        // authenticated agent can read this (not gated behind TenantAdmin like
        // /admin/api-definitions) since any agent can build flows. A general definition is a
        // connection (base URL, auth); its endpoints are the individual callable operations —
        // that's what the api_call / tf_general_api_call node dropdown picks from.
        group.MapGet("/general-apis", async (
            ITenantApiDefinitionRepository tenantDefinitions,
            IPortalApiDefinitionRepository portalDefinitions,
            ITenantApiEndpointRepository tenantEndpoints,
            IPortalApiEndpointRepository portalEndpoints,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();

            var tenantDefs = (await tenantDefinitions.GetByCategoryAsync(ApiCategory.General, ct)).Where(d => d.IsActive);
            var portalDefs = (await portalDefinitions.GetByCategoryAsync(ApiCategory.General, ct)).Where(d => d.IsActive);

            var result = new List<GeneralApiEndpointSummary>();
            foreach (var def in tenantDefs)
            {
                var eps = await tenantEndpoints.GetByDefinitionAsync(def.Id, ct);
                result.AddRange(eps.Where(e => e.IsActive).Select(e =>
                    new GeneralApiEndpointSummary(e.Id, e.Name, def.Id, def.Name, def.Provider, "tenant")));
            }
            foreach (var def in portalDefs)
            {
                var eps = await portalEndpoints.GetByDefinitionAsync(def.Id, ct);
                result.AddRange(eps.Where(e => e.IsActive).Select(e =>
                    new GeneralApiEndpointSummary(e.Id, e.Name, def.Id, def.Name, def.Provider, "portal")));
            }

            return Results.Ok(result.OrderBy(r => r.Scope).ThenBy(r => r.DefinitionName).ThenBy(r => r.Name));
        });
    }

    private static string BuildFlowSnapshot(Flow f) => JsonSerializer.Serialize(
        new FlowSnapshot(f.Name, f.Definition, f.FlowDirection, f.FlowSubType));

    private static object ToResponse(this Flow f) => new
    {
        id             = f.Id,
        name           = f.Name,
        flow_type      = f.FlowType,
        flow_direction = f.FlowDirection,
        flow_sub_type  = f.FlowSubType,
        version        = f.Version,
        is_active      = f.IsActive,
        client_id      = f.ClientId,
        campaign_id    = f.CampaignId,
        created_at     = f.CreatedAt,
        updated_at     = f.UpdatedAt,
    };

    private static object ToDetailResponse(this Flow f) => new
    {
        id             = f.Id,
        name           = f.Name,
        flow_type      = f.FlowType,
        flow_direction = f.FlowDirection,
        flow_sub_type  = f.FlowSubType,
        version        = f.Version,
        is_active      = f.IsActive,
        client_id      = f.ClientId,
        campaign_id    = f.CampaignId,
        created_at     = f.CreatedAt,
        updated_at     = f.UpdatedAt,
        definition     = f.Definition,
    };
}

public record CreateFlowRequest(
    string Name,
    string FlowType,
    string Definition,
    Guid? ClientId,
    Guid? CampaignId,
    string? FlowDirection = null,
    string? FlowSubType = null);

public record UpdateFlowRequest(
    string Definition,
    string? Name = null,
    string? FlowDirection = null,
    string? FlowSubType = null);

public record FlowSnapshot(string Name, string Definition, string? FlowDirection, string? FlowSubType);

public record GeneralApiEndpointSummary(
    Guid Id,
    string Name,
    Guid DefinitionId,
    string DefinitionName,
    string? Provider,
    string Scope);
