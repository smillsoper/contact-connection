using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class CampaignsEndpoints
{
    public static IEndpointRouteBuilder MapCampaignsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/campaigns").RequireAuthorization();

        group.MapPost("",                             Create);
        group.MapGet("",                              GetAll);
        group.MapGet("{id:guid}",                     GetById);
        group.MapPut("{id:guid}",                     Update);
        group.MapPut("{id:guid}/flow",                SetFlow);
        group.MapDelete("{id:guid}/flow",             RemoveFlow);
        group.MapPut("{id:guid}/outbound-flow",       SetOutboundFlow);
        group.MapDelete("{id:guid}/outbound-flow",    RemoveOutboundFlow);
        group.MapPost("{id:guid}/activate",           Activate);
        group.MapPost("{id:guid}/pause",              Pause);
        group.MapPost("{id:guid}/deactivate",         Deactivate);

        // Agent assignments
        group.MapPost("{id:guid}/agents",                         AssignAgent);
        group.MapPost("{id:guid}/agents/bulk",                    BulkAssignAgents);
        group.MapPut("{id:guid}/agents/{agentId:guid}",           UpdateAgentProficiency);
        group.MapDelete("{id:guid}/agents/{agentId:guid}",        RemoveAgent);

        // Group assignments
        group.MapPost("{id:guid}/groups",                         AssignGroup);
        group.MapPut("{id:guid}/groups/{groupId:guid}",           UpdateGroupProficiency);
        group.MapDelete("{id:guid}/groups/{groupId:guid}",        RemoveGroup);

        return app;
    }

    // ── POST /api/v1/campaigns ───────────────────────────────────────────────

    private static async Task<IResult> Create(
        CreateCampaignRequest req,
        ICampaignRepository repo,
        IClientRepository clients,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var client = await clients.GetByIdAsync(req.ClientId, ct);
        if (client is null) return Results.NotFound(new { error = "Client not found." });

        var campaign = Campaign.Create(tenantContext.Current!.Id, req.ClientId, req.Name, req.Slug, req.Description);
        await repo.AddAsync(campaign, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/campaigns/{campaign.Id}", ToSummaryResponse(campaign));
    }

    // ── GET /api/v1/campaigns?clientId=x ────────────────────────────────────

    private static async Task<IResult> GetAll(
        Guid? clientId,
        ICampaignRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var list = await repo.GetAllAsync(clientId, ct);
        return Results.Ok(list.Select(ToSummaryResponse));
    }

    // ── GET /api/v1/campaigns/{id} ───────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        ICampaignRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdWithAssignmentsAsync(id, ct);
        return campaign is null ? Results.NotFound() : Results.Ok(ToDetailResponse(campaign));
    }

    // ── PUT /api/v1/campaigns/{id} ───────────────────────────────────────────

    private static async Task<IResult> Update(
        Guid id,
        UpdateCampaignRequest req,
        ICampaignRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();

        campaign.Update(
            req.Name, req.Description,
            req.Direction, req.DialMode, req.Priority, req.AfterCallWorkSeconds, req.CallerIdNumber,
            req.MaxQueueSize, req.QueueTimeoutSeconds, req.ServiceLevelThresholdSeconds,
            req.QueueAccelerationEnabled, req.QueueAccelerationIntervalSeconds, req.QueueAccelerationPriorityBoost);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(campaign));
    }

    // ── PUT /api/v1/campaigns/{id}/flow ─────────────────────────────────────

    private static async Task<IResult> SetFlow(
        Guid id, SetCampaignFlowRequest req,
        ICampaignRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        campaign.AssignFlow(req.FlowId);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(campaign));
    }

    // ── DELETE /api/v1/campaigns/{id}/flow ──────────────────────────────────

    private static async Task<IResult> RemoveFlow(
        Guid id, ICampaignRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        campaign.RemoveFlow();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(campaign));
    }

    // ── PUT /api/v1/campaigns/{id}/outbound-flow ────────────────────────────

    private static async Task<IResult> SetOutboundFlow(
        Guid id, SetCampaignFlowRequest req,
        ICampaignRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        campaign.AssignOutboundFlow(req.FlowId);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(campaign));
    }

    // ── DELETE /api/v1/campaigns/{id}/outbound-flow ─────────────────────────

    private static async Task<IResult> RemoveOutboundFlow(
        Guid id, ICampaignRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();
        campaign.RemoveOutboundFlow();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(campaign));
    }

    // ── Status transitions ───────────────────────────────────────────────────

    private static async Task<IResult> Activate(
        Guid id, ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var c = await repo.GetByIdAsync(id, ct);
        if (c is null) return Results.NotFound();
        c.Activate(); await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(c));
    }

    private static async Task<IResult> Pause(
        Guid id, ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var c = await repo.GetByIdAsync(id, ct);
        if (c is null) return Results.NotFound();
        c.Pause(); await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(c));
    }

    private static async Task<IResult> Deactivate(
        Guid id, ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var c = await repo.GetByIdAsync(id, ct);
        if (c is null) return Results.NotFound();
        c.Deactivate(); await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(c));
    }

    // ── Agent assignments ────────────────────────────────────────────────────

    private static async Task<IResult> AssignAgent(
        Guid id, AssignAgentRequest req,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();

        var existing = await repo.GetAgentAssignmentAsync(id, req.AgentId, ct);
        if (existing is not null)
        {
            if (existing.IsActive)
                return Results.Conflict(new { error = "Agent is already assigned to this campaign." });
            // Re-activate previously removed assignment
            existing.Activate();
            existing.SetProficiency(req.Proficiency);
            await repo.SaveChangesAsync(ct);
            return Results.Ok(ToAssignmentResponse(existing));
        }

        var assignment = AgentCampaignAssignment.Create(req.AgentId, id, req.Proficiency);
        await repo.AddAgentAssignmentAsync(assignment, ct);
        await repo.SaveChangesAsync(ct);
        return Results.Created("", ToAssignmentResponse(assignment));
    }

    private static async Task<IResult> BulkAssignAgents(
        Guid id, BulkAssignAgentsRequest req,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();

        int added = 0, updated = 0;
        foreach (var entry in req.Agents)
        {
            var existing = await repo.GetAgentAssignmentAsync(id, entry.AgentId, ct);
            if (existing is not null)
            {
                if (!existing.IsActive) { existing.Activate(); updated++; }
                existing.SetProficiency(entry.Proficiency);
                continue;
            }
            await repo.AddAgentAssignmentAsync(AgentCampaignAssignment.Create(entry.AgentId, id, entry.Proficiency), ct);
            added++;
        }
        await repo.SaveChangesAsync(ct);
        return Results.Ok(new { added, updated });
    }

    private static async Task<IResult> UpdateAgentProficiency(
        Guid id, Guid agentId, SetProficiencyRequest req,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var assignment = await repo.GetAgentAssignmentAsync(id, agentId, ct);
        if (assignment is null) return Results.NotFound(new { error = "Agent assignment not found." });

        assignment.SetProficiency(req.Proficiency);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToAssignmentResponse(assignment));
    }

    private static async Task<IResult> RemoveAgent(
        Guid id, Guid agentId,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var assignment = await repo.GetAgentAssignmentAsync(id, agentId, ct);
        if (assignment is null) return Results.NotFound(new { error = "Agent assignment not found." });

        assignment.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── Group assignments ────────────────────────────────────────────────────

    private static async Task<IResult> AssignGroup(
        Guid id, AssignGroupRequest req,
        ICampaignRepository repo, IAgentGroupRepository groups, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();

        if (await groups.GetByIdAsync(req.GroupId, ct) is null)
            return Results.NotFound(new { error = "Group not found." });

        var existing = await repo.GetGroupAssignmentAsync(id, req.GroupId, ct);
        if (existing is not null)
            return Results.Conflict(new { error = "Group is already assigned to this campaign." });

        var assignment = GroupCampaignAssignment.Create(req.GroupId, id, req.Proficiency);
        await repo.AddGroupAssignmentAsync(assignment, ct);
        await repo.SaveChangesAsync(ct);
        return Results.Created("", ToGroupAssignmentResponse(assignment));
    }

    private static async Task<IResult> UpdateGroupProficiency(
        Guid id, Guid groupId, SetProficiencyRequest req,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var assignment = await repo.GetGroupAssignmentAsync(id, groupId, ct);
        if (assignment is null) return Results.NotFound(new { error = "Group assignment not found." });

        assignment.SetProficiency(req.Proficiency);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToGroupAssignmentResponse(assignment));
    }

    private static async Task<IResult> RemoveGroup(
        Guid id, Guid groupId,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var assignment = await repo.GetGroupAssignmentAsync(id, groupId, ct);
        if (assignment is null) return Results.NotFound(new { error = "Group assignment not found." });

        assignment.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── Response shapes ──────────────────────────────────────────────────────

    internal static object ToSummaryResponse(Campaign c) => new
    {
        c.Id, c.TenantId, c.ClientId, c.Name, c.Slug, c.Status, c.Description, c.FlowId, c.OutboundFlowId,
        c.Direction, c.DialMode, c.CallerIdNumber, c.Priority, c.AfterCallWorkSeconds,
        c.MaxQueueSize, c.QueueTimeoutSeconds, c.ServiceLevelThresholdSeconds,
        c.QueueAccelerationEnabled, c.QueueAccelerationIntervalSeconds, c.QueueAccelerationPriorityBoost,
        Client = c.Client is null ? null : new { c.Client.Id, c.Client.Name },
        c.CreatedAt, c.UpdatedAt
    };

    internal static object ToDetailResponse(Campaign c) => new
    {
        c.Id, c.TenantId, c.ClientId, c.Name, c.Slug, c.Status, c.Description, c.FlowId, c.OutboundFlowId,
        c.Direction, c.DialMode, c.CallerIdNumber, c.Priority, c.AfterCallWorkSeconds,
        c.MaxQueueSize, c.QueueTimeoutSeconds, c.ServiceLevelThresholdSeconds,
        c.QueueAccelerationEnabled, c.QueueAccelerationIntervalSeconds, c.QueueAccelerationPriorityBoost,
        Client = c.Client is null ? null : new { c.Client.Id, c.Client.Name },
        PhoneNumbers     = c.PhoneNumbers.Select(p => new { p.Id, p.Number, p.Label, p.IsActive }),
        AgentAssignments = c.AgentAssignments.Where(a => a.IsActive).Select(ToAssignmentResponse),
        GroupAssignments = c.GroupAssignments.Where(a => a.IsActive).Select(ToGroupAssignmentResponse),
        c.CreatedAt, c.UpdatedAt
    };

    private static object ToAssignmentResponse(AgentCampaignAssignment a) => new
    { a.Id, a.AgentId, a.CampaignId, a.Proficiency, a.IsActive, a.AssignedAt };

    private static object ToGroupAssignmentResponse(GroupCampaignAssignment a) => new
    { a.Id, a.GroupId, a.CampaignId, a.Proficiency, a.IsActive, a.AssignedAt,
      Group = a.Group is null ? null : new { a.Group.Id, a.Group.Name } };
}

public record CreateCampaignRequest(Guid ClientId, string Name, string Slug, string? Description = null);
public record UpdateCampaignRequest(
    string Name,
    string? Description,
    string Direction = "inbound",
    string DialMode = "manual",
    int Priority = 5,
    int AfterCallWorkSeconds = 30,
    string? CallerIdNumber = null,
    int MaxQueueSize = 50,
    int QueueTimeoutSeconds = 300,
    int ServiceLevelThresholdSeconds = 30,
    bool QueueAccelerationEnabled = false,
    int QueueAccelerationIntervalSeconds = 60,
    int QueueAccelerationPriorityBoost = 1);
public record SetCampaignFlowRequest(Guid FlowId);
public record AssignAgentRequest(Guid AgentId, int Proficiency = 50);
public record BulkAssignAgentsRequest(List<BulkAgentEntry> Agents);
public record BulkAgentEntry(Guid AgentId, int Proficiency = 50);
public record AssignGroupRequest(Guid GroupId, int Proficiency = 50);
public record SetProficiencyRequest(int Proficiency);
