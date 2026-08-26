using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// One agent's effective proficiency for a campaign — the higher of their direct
/// AgentCampaignAssignment.Proficiency and the best Proficiency among their active group
/// assignments — plus AvailableSince (the Redis agent state's SetAt while Available), used as
/// the longest-idle tie-break.
/// </summary>
public record RankedAgent(Guid AgentId, int EffectiveProficiency, DateTimeOffset AvailableSince);

/// <summary>
/// Builds the ranked, currently-Available agent list for a campaign — shared by
/// RouteToQueueNodeHandler (initial queue entry) and QueuePollingService (re-poll on newly
/// available agents), which previously duplicated this logic independently and, in doing so,
/// both missed filtering GroupCampaignAssignment.IsActive (only the direct-assignment query
/// filtered IsActive; a deactivated group assignment still contributed ringable agents). Session
/// 92 — see API delivery-mode work in CLAUDE.md / DevLog.
///
/// Ranking: effective proficiency DESC, tie-broken by longest idle (earliest AvailableSince)
/// among agents currently in AgentStateCodes.Available. An agent whose Redis state is anything
/// else (or has never set one) is excluded entirely — same as the original inline logic.
/// </summary>
public class EligibleAgentRanker(IAgentStateStore stateStore)
{
    public async Task<IReadOnlyList<RankedAgent>> GetRankedEligibleAgentsAsync(
        TenantDbContext db, Guid tenantId, Guid campaignId,
        IReadOnlySet<Guid>? excludeAgentIds = null, CancellationToken ct = default)
    {
        var direct = await db.AgentCampaignAssignments
            .Where(a => a.CampaignId == campaignId && a.IsActive)
            .Select(a => new { a.AgentId, a.Proficiency })
            .ToListAsync(ct);

        // Bug fix: the original inline queries (RouteToQueueNodeHandler, QueuePollingService)
        // never filtered g.IsActive here — a deactivated group assignment still rang its members.
        var groups = await db.GroupCampaignAssignments
            .Where(g => g.CampaignId == campaignId && g.IsActive)
            .Select(g => new { g.GroupId, g.Proficiency })
            .ToListAsync(ct);

        var groupMembers = groups.Count == 0
            ? []
            : await db.AgentGroupMembers
                .Where(m => groups.Select(g => g.GroupId).Contains(m.GroupId))
                .ToListAsync(ct);

        // Effective proficiency = MAX(direct, best active-group) per agent — an agent assigned
        // both directly and via a group takes whichever proficiency is higher.
        var effective = new Dictionary<Guid, int>();
        foreach (var d in direct) effective[d.AgentId] = d.Proficiency;
        foreach (var m in groupMembers)
        {
            var proficiency = groups.First(g => g.GroupId == m.GroupId).Proficiency;
            effective[m.AgentId] = effective.TryGetValue(m.AgentId, out var existing)
                ? Math.Max(existing, proficiency)
                : proficiency;
        }

        var ranked = new List<RankedAgent>();
        foreach (var (agentId, proficiency) in effective)
        {
            if (excludeAgentIds?.Contains(agentId) == true) continue;

            var state = await stateStore.GetAsync(tenantId, agentId, ct);
            if (state?.Code != AgentStateCodes.Available) continue;

            ranked.Add(new RankedAgent(agentId, proficiency, state.SetAt));
        }

        return ranked
            .OrderByDescending(r => r.EffectiveProficiency)
            .ThenBy(r => r.AvailableSince) // earliest SetAt while Available = longest idle
            .ToList();
    }
}
