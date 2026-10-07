using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>Which agents are dedicated to which campaigns right now (S183) — see <see cref="AgentDedication"/>.</summary>
public static class AgentDedications
{
    /// <summary>Proficiency for an agent taking a dedicated campaign's calls without an assignment to it (the assignment
    /// default).</summary>
    public const int DefaultProficiency = 50;

    /// <summary>Agent → the campaigns they're dedicated to right now (only agents with an active dedication).</summary>
    public static async Task<Dictionary<Guid, HashSet<Guid>>> ActiveAsync(
        TenantDbContext db, DateTimeOffset now, Guid? onlyAgentId = null, CancellationToken ct = default)
    {
        var open = await db.AgentDedications.AsNoTracking()
            .Where(d => d.EndedAt == null && d.StartsAt <= now && (d.EndsAt == null || d.EndsAt > now)
                        && (onlyAgentId == null || d.AgentId == onlyAgentId))
            .ToListAsync(ct);
        var result = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var d in open.Where(d => d.IsActiveAt(now)))
        {
            if (!result.TryGetValue(d.AgentId, out var set)) result[d.AgentId] = set = [];
            set.UnionWith(d.CampaignIds);
        }
        return result;
    }
}
