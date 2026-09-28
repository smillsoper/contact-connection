using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class AgentGroupsEndpoints
{
    public static IEndpointRouteBuilder MapAgentGroupsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agent-groups").RequireAuthorization();

        group.MapPost("",                                       Create);
        group.MapGet("",                                        GetAll);
        group.MapGet("{id:guid}",                               GetById);
        group.MapPut("{id:guid}",                               Update);
        group.MapPost("{id:guid}/activate",                     Activate);
        group.MapPost("{id:guid}/deactivate",                   Deactivate);
        group.MapPost("{id:guid}/members",                      AddMember);
        group.MapDelete("{id:guid}/members/{agentId:guid}",     RemoveMember);
        group.MapGet("{id:guid}/member-campaigns",               GetMemberCampaigns);
        group.MapPut("{id:guid}/members/{agentId:guid}/campaigns", SetMemberCampaigns).RequireAuthorization("TenantAdmin");

        return app;
    }

    // ── POST /api/v1/agent-groups ────────────────────────────────────────────

    private static async Task<IResult> Create(
        CreateAgentGroupRequest req,
        IAgentGroupRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        var group = AgentGroup.Create(ctx.Current!.Id, req.Name, req.Slug, req.Description);
        await repo.AddAsync(group, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/agent-groups/{group.Id}", ToSummaryResponse(group));
    }

    // ── GET /api/v1/agent-groups ─────────────────────────────────────────────

    private static async Task<IResult> GetAll(
        IAgentGroupRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var list = await repo.GetAllAsync(ct);
        return Results.Ok(list.Select(ToSummaryResponse));
    }

    // ── GET /api/v1/agent-groups/{id} ────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        IAgentGroupRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var group = await repo.GetByIdWithMembersAsync(id, ct);
        return group is null ? Results.NotFound() : Results.Ok(ToDetailResponse(group));
    }

    // ── PUT /api/v1/agent-groups/{id} ────────────────────────────────────────

    private static async Task<IResult> Update(
        Guid id,
        UpdateAgentGroupRequest req,
        IAgentGroupRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var group = await repo.GetByIdAsync(id, ct);
        if (group is null) return Results.NotFound();

        group.Update(req.Name, req.Description);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(group));
    }

    // ── Status transitions ────────────────────────────────────────────────────

    private static async Task<IResult> Activate(
        Guid id, IAgentGroupRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var g = await repo.GetByIdAsync(id, ct);
        if (g is null) return Results.NotFound();
        g.Activate(); await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(g));
    }

    private static async Task<IResult> Deactivate(
        Guid id, IAgentGroupRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var g = await repo.GetByIdAsync(id, ct);
        if (g is null) return Results.NotFound();
        g.Deactivate(); await repo.SaveChangesAsync(ct);
        return Results.Ok(ToSummaryResponse(g));
    }

    // ── POST /api/v1/agent-groups/{id}/members ───────────────────────────────

    private static async Task<IResult> AddMember(
        Guid id,
        AddGroupMemberRequest req,
        IAgentGroupRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();

        var existing = await repo.GetMemberAsync(id, req.AgentId, ct);
        if (existing is not null)
            return Results.Conflict(new { error = "Agent is already a member of this group." });

        var member = AgentGroupMember.Create(id, req.AgentId);
        await repo.AddMemberAsync(member, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created("", new { member.GroupId, member.AgentId, member.JoinedAt });
    }

    // ── DELETE /api/v1/agent-groups/{id}/members/{agentId} ───────────────────

    private static async Task<IResult> RemoveMember(
        Guid id,
        Guid agentId,
        IAgentGroupRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var member = await repo.GetMemberAsync(id, agentId, ct);
        if (member is null) return Results.NotFound(new { error = "Member not found." });

        await repo.RemoveMemberAsync(member, ct);
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── GET /api/v1/agent-groups/{id}/member-campaigns ──────────────────────
    // The group's campaigns (with routing tier) and, per member, which of them they may take —
    // everything the member-campaign grid needs in one call.
    private static async Task<IResult> GetMemberCampaigns(
        Guid id, IAgentGroupRepository repo, ICampaignRepository campaigns, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var group = await repo.GetByIdWithMembersAsync(id, ct);
        if (group is null) return Results.NotFound();

        var assignments = await repo.GetActiveCampaignAssignmentsAsync(id, ct);
        var exclusions = await repo.GetMemberExclusionsAsync(id, ct);
        var campaignNames = new Dictionary<Guid, string>();
        foreach (var a in assignments)
            campaignNames[a.CampaignId] = (await campaigns.GetByIdAsync(a.CampaignId, ct))?.Name ?? "(deleted campaign)";

        return Results.Ok(new
        {
            Campaigns = assignments.Select(a => new
            {
                a.CampaignId, CampaignName = campaignNames[a.CampaignId],
                a.RoutingTier, a.ExclusiveWindowSeconds, a.TierLabel, a.Proficiency,
            }).OrderByDescending(c => c.RoutingTier).ThenBy(c => c.CampaignName),
            Members = group.Members.Select(m => new
            {
                m.AgentId,
                AllowedCampaignIds = assignments.Select(a => a.CampaignId)
                    .Where(c => !exclusions.Any(e => e.AgentId == m.AgentId && e.CampaignId == c)),
            }),
        });
    }

    // ── PUT /api/v1/agent-groups/{id}/members/{agentId}/campaigns ───────────
    // Body: the campaigns this member MAY take through the group; every other assigned campaign is
    // stored as an exclusion (see AgentGroupMemberCampaignExclusion).
    private static async Task<IResult> SetMemberCampaigns(
        Guid id, Guid agentId, SetMemberCampaignsRequest req,
        IAgentGroupRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.GetMemberAsync(id, agentId, ct) is null) return Results.NotFound(new { error = "Member not found." });

        var assigned = (await repo.GetActiveCampaignAssignmentsAsync(id, ct)).Select(a => a.CampaignId).ToHashSet();
        var allowed = (req.AllowedCampaignIds ?? []).ToHashSet();
        if (allowed.Except(assigned).Any())
            return Results.BadRequest(new { error = "One or more campaigns aren't assigned to this group." });

        await repo.SetMemberExclusionsAsync(id, agentId, assigned.Except(allowed).ToList(), ct);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(new { agentId, AllowedCampaignIds = allowed });
    }

    // ── Response shapes ───────────────────────────────────────────────────────

    internal static object ToSummaryResponse(AgentGroup g) => new
    {
        g.Id, g.TenantId, g.Name, g.Slug, g.Description, g.IsActive,
        MemberCount = g.Members.Count,
        g.CreatedAt, g.UpdatedAt
    };

    internal static object ToDetailResponse(AgentGroup g) => new
    {
        g.Id, g.TenantId, g.Name, g.Slug, g.Description, g.IsActive,
        Members = g.Members.Select(m => new { m.GroupId, m.AgentId, m.JoinedAt }),
        CampaignAssignments = g.CampaignAssignments.Select(a => new
            { a.Id, a.CampaignId, a.Proficiency, a.IsActive, a.AssignedAt }),
        g.CreatedAt, g.UpdatedAt
    };
}

public record CreateAgentGroupRequest(string Name, string Slug, string? Description = null);
public record UpdateAgentGroupRequest(string Name, string? Description = null);
public record AddGroupMemberRequest(Guid AgentId);

public record SetMemberCampaignsRequest(List<Guid>? AllowedCampaignIds);
