using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Bulk campaign assignment (S182): agents and agent groups × campaigns in one grid, with bulk assign / set proficiency /
/// remove. Same rules as the per-campaign endpoints — removing deactivates the row (its history stays), assigning again
/// revives it.
/// </summary>
public static class AssignmentMatrixEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentMatrixEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/assignments").RequireAuthorization("TenantAdmin");
        group.MapGet("matrix", GetMatrix);
        group.MapPost("bulk",  Bulk);
        return app;
    }

    private static async Task<IResult> GetMatrix(Guid? clientId, ScopedTenantDbContextFactory dbf, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var clients = await db.Clients.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var clientNames = clients.ToDictionary(c => c.Id, c => c.Name);
        var campaigns = await db.Campaigns.AsNoTracking()
            .Where(c => clientId == null || c.ClientId == clientId)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.ClientId, c.Status }).ToListAsync(ct);
        var campaignIds = campaigns.Select(c => c.Id).ToList();

        var agentCells = await db.AgentCampaignAssignments.AsNoTracking()
            .Where(a => a.IsActive && campaignIds.Contains(a.CampaignId))
            .Select(a => new { a.AgentId, a.CampaignId, a.Proficiency }).ToListAsync(ct);
        var groupCells = await db.GroupCampaignAssignments.AsNoTracking()
            .Where(a => a.IsActive && campaignIds.Contains(a.CampaignId))
            .Select(a => new { a.GroupId, a.CampaignId, a.Proficiency, a.RoutingTier, a.TierLabel }).ToListAsync(ct);
        var agents = await db.Agents.AsNoTracking().Where(a => a.IsActive)
            .OrderBy(a => a.FirstName).ThenBy(a => a.LastName)
            .Select(a => new { a.Id, a.FirstName, a.LastName, a.Email, a.Role }).ToListAsync(ct);
        var groups = await db.AgentGroups.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name }).ToListAsync(ct);

        var byAgent = agentCells.ToLookup(c => c.AgentId);
        var byGroup = groupCells.ToLookup(c => c.GroupId);
        return Results.Ok(new
        {
            clients,
            campaigns = campaigns.Select(c => new { c.Id, c.Name, c.ClientId, clientName = clientNames.GetValueOrDefault(c.ClientId), c.Status }),
            agents = agents.Select(a => new
            {
                a.Id, name = $"{a.FirstName} {a.LastName}".Trim(), a.Email, a.Role,
                cells = byAgent[a.Id].ToDictionary(c => c.CampaignId, c => c.Proficiency),
            }),
            groups = groups.Select(g => new
            {
                g.Id, g.Name,
                cells = byGroup[g.Id].ToDictionary(c => c.CampaignId, c => new { c.Proficiency, c.RoutingTier, c.TierLabel }),
            }),
        });
    }

    private static async Task<IResult> Bulk(BulkAssignmentRequest req, ScopedTenantDbContextFactory dbf, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (req.Action is not ("assign" or "set" or "remove")) return Results.BadRequest(new { error = $"Unknown action '{req.Action}'." });
        if (req.Action is "assign" or "set" && req.Proficiency is not (>= 1 and <= 100))
            return Results.BadRequest(new { error = "Proficiency must be 1–100." });
        var agentIds = req.AgentIds ?? [];
        var groupIds = req.GroupIds ?? [];
        if (req.CampaignIds.Count == 0 || agentIds.Count + groupIds.Count == 0)
            return Results.BadRequest(new { error = "Choose at least one campaign and one agent or group." });

        await using var db = dbf.Create();
        var campaignIds = await db.Campaigns.Where(c => req.CampaignIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        agentIds = await db.Agents.Where(a => agentIds.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        groupIds = await db.AgentGroups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);

        var agentRows = await db.AgentCampaignAssignments
            .Where(a => agentIds.Contains(a.AgentId) && campaignIds.Contains(a.CampaignId)).ToListAsync(ct);
        var groupRows = await db.GroupCampaignAssignments
            .Where(a => groupIds.Contains(a.GroupId) && campaignIds.Contains(a.CampaignId)).ToListAsync(ct);

        int added = 0, updated = 0, removed = 0;
        foreach (var campaignId in campaignIds)
        {
            foreach (var agentId in agentIds)
            {
                var row = agentRows.FirstOrDefault(a => a.AgentId == agentId && a.CampaignId == campaignId);
                switch (req.Action)
                {
                    case "assign" when row is null:
                        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agentId, campaignId, req.Proficiency!.Value)); added++; break;
                    case "assign":
                        if (!row.IsActive) { row.Activate(); added++; } else updated++;
                        row.SetProficiency(req.Proficiency!.Value); break;
                    case "set" when row is { IsActive: true }:
                        row.SetProficiency(req.Proficiency!.Value); updated++; break;
                    case "remove" when row is { IsActive: true }:
                        row.Deactivate(); removed++; break;
                }
            }
            foreach (var groupId in groupIds)
            {
                var row = groupRows.FirstOrDefault(a => a.GroupId == groupId && a.CampaignId == campaignId);
                switch (req.Action)
                {
                    case "assign" when row is null:
                        db.GroupCampaignAssignments.Add(GroupCampaignAssignment.Create(groupId, campaignId, req.Proficiency!.Value)); added++; break;
                    case "assign":
                        if (!row.IsActive) { row.Activate(); added++; } else updated++;
                        row.SetProficiency(req.Proficiency!.Value); break;
                    case "set" when row is { IsActive: true }:
                        row.SetProficiency(req.Proficiency!.Value); updated++; break;
                    case "remove" when row is { IsActive: true }:
                        row.Deactivate(); removed++; break;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { added, updated, removed });
    }
}

public record BulkAssignmentRequest(List<Guid> CampaignIds, List<Guid>? AgentIds, List<Guid>? GroupIds, string Action, int? Proficiency = null);
