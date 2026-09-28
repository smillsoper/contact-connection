using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// One agent's effective proficiency for a campaign — the higher of their direct
/// AgentCampaignAssignment.Proficiency and the best Proficiency among their active group
/// assignments — plus AvailableSince (the Redis agent state's SetAt while Available), used as
/// the longest-idle tie-break.
///
/// Parallel queuing (docs/design/parallel-queuing.md): <see cref="Tier"/> is the agent's best
/// routing tier for the campaign (direct assignment = 0; via a group = that assignment's
/// RoutingTier), and <see cref="GroupId"/>/<see cref="TierLabel"/> identify the route that gave
/// it — what the call record is stamped with when this agent wins the call.
/// </summary>
public record RankedAgent(
    Guid AgentId, int EffectiveProficiency, DateTimeOffset AvailableSince,
    int Tier = 0, Guid? GroupId = null, string? TierLabel = null);

/// <summary>One agent's route to a campaign — see <see cref="EligibleAgentRanker.ResolveRouteAsync"/>.</summary>
public record AgentRoute(int Tier, Guid? GroupId, string? TierLabel, int Proficiency);

/// <summary>A priority tier holding new calls exclusively for its first N seconds.</summary>
public record TierWindow(int Tier, int Seconds);

/// <summary>
/// Who a queued call is offered to right now: the eligible agents in the highest tier that has
/// anyone available (<see cref="OfferTier"/>). Empty with <see cref="HeldForTier"/> set when an
/// exclusive window is holding the call for a tier that has nobody free yet.
/// </summary>
public record OfferSet(IReadOnlyList<RankedAgent> Agents, int? OfferTier, int? HeldForTier);

/// <summary>
/// Builds the ranked, currently-Available agent list for a campaign — shared by
/// RouteToQueueNodeHandler (initial queue entry) and QueuePollingService (re-poll on newly
/// available agents), which previously duplicated this logic independently and, in doing so,
/// both missed filtering GroupCampaignAssignment.IsActive (only the direct-assignment query
/// filtered IsActive; a deactivated group assignment still contributed ringable agents). Session
/// 92 — see API delivery-mode work in CLAUDE.md / DevLog.
///
/// Ranking: routing tier DESC, then effective proficiency DESC, tie-broken by longest idle
/// (earliest AvailableSince) among agents currently in AgentStateCodes.Available. An agent whose
/// Redis state is anything else (or has never set one) is excluded entirely — same as the
/// original inline logic. A group member with an AgentGroupMemberCampaignExclusion for the
/// campaign gets no route through that group.
/// </summary>
public class EligibleAgentRanker(IAgentStateStore stateStore)
{
    /// <param name="restrictGroupId">"Only offer to this agent group" (tf_route_to_queue's
    /// agentGroupId — e.g. Elite): only that group's allowed members are eligible; direct
    /// assignments and other groups don't count, and there's no fallback.</param>
    public async Task<IReadOnlyList<RankedAgent>> GetRankedEligibleAgentsAsync(
        TenantDbContext db, Guid tenantId, Guid campaignId,
        IReadOnlySet<Guid>? excludeAgentIds = null, Guid? restrictGroupId = null, CancellationToken ct = default)
        => (await LoadAsync(db, tenantId, campaignId, excludeAgentIds, restrictGroupId, ct)).Ranked;

    /// <summary>The tier-aware offer for a queued call that has waited <paramref name="secondsWaited"/>
    /// — see <see cref="SelectOffer"/>.</summary>
    public async Task<OfferSet> GetOfferSetAsync(
        TenantDbContext db, Guid tenantId, Guid campaignId, double secondsWaited,
        IReadOnlySet<Guid>? excludeAgentIds = null, Guid? restrictGroupId = null, CancellationToken ct = default)
    {
        var (ranked, windows) = await LoadAsync(db, tenantId, campaignId, excludeAgentIds, restrictGroupId, ct);
        // A group restriction already pins the call to one group — exclusive windows don't apply.
        return SelectOffer(ranked, restrictGroupId is null ? windows : [], secondsWaited);
    }

    /// <summary>
    /// Pure priority: offer to the highest tier that has anyone available. An exclusive window
    /// (tier T, N seconds) that hasn't elapsed yet holds the call for tier T or higher — lower
    /// tiers get nothing until it does, even if they're free.
    /// </summary>
    public static OfferSet SelectOffer(IReadOnlyList<RankedAgent> ranked, IReadOnlyList<TierWindow> windows, double secondsWaited)
    {
        int? heldFor = windows.Where(w => secondsWaited < w.Seconds).Select(w => (int?)w.Tier).Max();
        if (ranked.Count == 0) return new OfferSet([], null, heldFor);

        var topTier = ranked.Max(r => r.Tier);
        if (heldFor is { } h && topTier < h) return new OfferSet([], null, heldFor);

        return new OfferSet(ranked.Where(r => r.Tier == topTier).ToList(), topTier, heldFor);
    }

    /// <summary>
    /// The route an agent takes a call on this campaign through — their highest-tier route
    /// (direct = tier 0), ties to the higher proficiency — or null if they have none (or, with
    /// <paramref name="restrictGroupId"/>, aren't an allowed member of that group). DB-only, no
    /// availability check: used at delivery to stamp CallRecord.RoutedGroupId/Tier/TierLabel.
    /// </summary>
    public static async Task<AgentRoute?> ResolveRouteAsync(
        TenantDbContext db, Guid campaignId, Guid agentId, Guid? restrictGroupId = null, CancellationToken ct = default)
    {
        var routes = await LoadRoutesAsync(db, campaignId, restrictGroupId, agentId, ct);
        return routes.TryGetValue(agentId, out var list) ? BestRoute(list) : null;
    }

    /// <summary>Every agent with a route to the campaign (optionally through one group only),
    /// mapped to their best route — no availability filter. Used by external-routing stats.</summary>
    public static async Task<Dictionary<Guid, AgentRoute>> GetBestRoutesAsync(
        TenantDbContext db, Guid campaignId, Guid? restrictGroupId = null, CancellationToken ct = default)
        => (await LoadRoutesAsync(db, campaignId, restrictGroupId, onlyAgentId: null, ct))
            .ToDictionary(kv => kv.Key, kv => BestRoute(kv.Value));

    /// <summary>Whether any agent with a route to the campaign is logged in at all (any state but
    /// logged_out — available, on a call, in ACW, on break…). CXone's "CheckAgents" step: nobody
    /// logged in means nobody will ever answer, unlike "nobody available right now".</summary>
    public async Task<bool> AnyLoggedInAsync(TenantDbContext db, Guid tenantId, Guid campaignId, CancellationToken ct = default)
    {
        foreach (var agentId in (await LoadRoutesAsync(db, campaignId, null, onlyAgentId: null, ct)).Keys)
        {
            var state = await stateStore.GetAsync(tenantId, agentId, ct);
            if (state is not null && state.Code != AgentStateCodes.LoggedOut) return true;
        }
        return false;
    }

    private async Task<(IReadOnlyList<RankedAgent> Ranked, IReadOnlyList<TierWindow> Windows)> LoadAsync(
        TenantDbContext db, Guid tenantId, Guid campaignId,
        IReadOnlySet<Guid>? excludeAgentIds, Guid? restrictGroupId, CancellationToken ct)
    {
        var routes = await LoadRoutesAsync(db, campaignId, restrictGroupId, onlyAgentId: null, ct);

        var ranked = new List<RankedAgent>();
        foreach (var (agentId, agentRoutes) in routes)
        {
            if (excludeAgentIds?.Contains(agentId) == true) continue;

            var state = await stateStore.GetAsync(tenantId, agentId, ct);
            if (state?.Code != AgentStateCodes.Available) continue;

            // Effective proficiency = MAX over every route (direct and each group) — unchanged;
            // the tier/group/label come from the agent's best (highest-tier) route.
            var best = BestRoute(agentRoutes);
            ranked.Add(new RankedAgent(
                agentId, agentRoutes.Max(r => r.Proficiency), state.SetAt, best.Tier, best.GroupId, best.TierLabel));
        }

        var windows = await db.GroupCampaignAssignments
            .Where(g => g.CampaignId == campaignId && g.IsActive && g.RoutingTier > 0 && g.ExclusiveWindowSeconds != null)
            .Select(g => new TierWindow(g.RoutingTier, g.ExclusiveWindowSeconds!.Value))
            .ToListAsync(ct);

        return (ranked
            .OrderByDescending(r => r.Tier)
            .ThenByDescending(r => r.EffectiveProficiency)
            .ThenBy(r => r.AvailableSince) // earliest SetAt while Available = longest idle
            .ToList(), windows);
    }

    /// <summary>Every route each agent has to the campaign: direct assignment (tier 0) and each
    /// active group assignment they're a member of and not excluded from.</summary>
    private static async Task<Dictionary<Guid, List<AgentRoute>>> LoadRoutesAsync(
        TenantDbContext db, Guid campaignId, Guid? restrictGroupId, Guid? onlyAgentId, CancellationToken ct)
    {
        var routes = new Dictionary<Guid, List<AgentRoute>>();
        void Add(Guid agentId, AgentRoute route)
        {
            if (!routes.TryGetValue(agentId, out var list)) routes[agentId] = list = [];
            list.Add(route);
        }

        if (restrictGroupId is null)
        {
            var direct = await db.AgentCampaignAssignments
                .Where(a => a.CampaignId == campaignId && a.IsActive && (onlyAgentId == null || a.AgentId == onlyAgentId))
                .Select(a => new { a.AgentId, a.Proficiency })
                .ToListAsync(ct);
            foreach (var d in direct) Add(d.AgentId, new AgentRoute(0, null, null, d.Proficiency));
        }

        // Bug fix: the original inline queries (RouteToQueueNodeHandler, QueuePollingService)
        // never filtered g.IsActive here — a deactivated group assignment still rang its members.
        var groups = await db.GroupCampaignAssignments
            .Where(g => g.CampaignId == campaignId && g.IsActive && (restrictGroupId == null || g.GroupId == restrictGroupId))
            .Select(g => new { g.GroupId, g.Proficiency, g.RoutingTier, g.TierLabel })
            .ToListAsync(ct);
        if (groups.Count == 0) return routes;

        var groupIds = groups.Select(g => g.GroupId).ToList();
        var members = await db.AgentGroupMembers
            .Where(m => groupIds.Contains(m.GroupId) && (onlyAgentId == null || m.AgentId == onlyAgentId))
            .Select(m => new { m.GroupId, m.AgentId })
            .ToListAsync(ct);
        var excluded = (await db.AgentGroupMemberCampaignExclusions
                .Where(e => e.CampaignId == campaignId && groupIds.Contains(e.GroupId))
                .Select(e => new { e.GroupId, e.AgentId })
                .ToListAsync(ct))
            .Select(e => (e.GroupId, e.AgentId))
            .ToHashSet();

        foreach (var m in members)
        {
            if (excluded.Contains((m.GroupId, m.AgentId))) continue;
            var g = groups.First(x => x.GroupId == m.GroupId);
            // A tier-0 group's label (e.g. "Elite") only means something when the call was pinned
            // to that group — an Elite member answering an ordinary call through the regular pool
            // must not be stamped "Elite" (commissions key on the label).
            var label = restrictGroupId is null && g.RoutingTier == 0 ? null : g.TierLabel;
            Add(m.AgentId, new AgentRoute(g.RoutingTier, g.GroupId, label, g.Proficiency));
        }
        return routes;
    }

    private static AgentRoute BestRoute(List<AgentRoute> routes) =>
        routes.OrderByDescending(r => r.Tier).ThenByDescending(r => r.Proficiency).First();
}
