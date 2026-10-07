using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Agent-group and DNIS filters for dashboard widgets (S181). Both narrow on top of the client / campaign filter.
/// Agent group: reporting widgets (KPIs, Service Level, Call Records) use the groups each interaction's agent was in at the
/// time of the call (CallInteraction.AgentGroupIds, or the group it was routed through); live widgets (agents, Active Calls)
/// use today's members. DNIS: the number the caller dialed, compared on its last 10 digits.
/// </summary>
public static class WidgetFilters
{
    /// <summary>"dnis" query value: comma-separated numbers → PhoneKey keys; null = no filter.</summary>
    public static HashSet<string>? Dnis(string? csv) => PhoneKey.Set(csv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>The group's members; null when no group is chosen (no filter). An unknown group matches nobody.</summary>
    public static async Task<HashSet<Guid>?> GroupAgentsAsync(Guid? groupId, IAgentGroupRepository groups, CancellationToken ct)
    {
        if (groupId is not { } id) return null;
        var group = await groups.GetByIdWithMembersAsync(id, ct);
        return group?.Members.Select(m => m.AgentId).ToHashSet() ?? [];
    }

    public static IEndpointRouteBuilder MapWidgetFilterOptions(this IEndpointRouteBuilder app)
    {
        // The numbers a widget can filter on: the tenant's phone numbers, narrowed to a client / campaign when chosen.
        app.MapGet("/api/v1/dashboard-widgets/dnis-options", async (Guid? clientId, Guid? campaignId, ScopedTenantDbContextFactory dbf,
            TenantContext tc, CancellationToken ct) =>
        {
            if (!tc.HasTenant) return Results.Unauthorized();
            await using var db = dbf.Create();
            var campaigns = await db.Campaigns.AsNoTracking()
                .Where(c => (campaignId == null || c.Id == campaignId) && (clientId == null || c.ClientId == clientId))
                .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var ids = campaigns.Keys.ToList();
            var numbers = await db.PhoneNumbers.AsNoTracking().Where(n => n.CampaignId != null && ids.Contains(n.CampaignId.Value))
                .OrderBy(n => n.Number).Select(n => new { n.Number, n.Label, n.CampaignId, n.IsActive }).ToListAsync(ct);
            return Results.Ok(numbers.Select(n => new { n.Number, n.Label, campaign = campaigns.GetValueOrDefault(n.CampaignId!.Value), n.IsActive }));
        }).RequireAuthorization("ReportsView");
        return app;
    }
}
