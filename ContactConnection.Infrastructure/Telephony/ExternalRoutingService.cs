using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;

namespace ContactConnection.Infrastructure.Telephony;

public record TierAvailability(int Tier, string? Label, int LoggedIn, int Available);

/// <summary>Live availability for a campaign (optionally one agent group) — the same agents and
/// queue sessions the queue engine itself uses, so an external router's answer always agrees with
/// what the queue will actually do.</summary>
public record CampaignAvailability(
    int LoggedIn, int Available, int Unavailable, int Queued, int LongestWaitSeconds,
    IReadOnlyList<TierAvailability> Tiers);

public record RoutingDecision(bool Accepted, string Reason);

/// <summary>
/// Backs the external-routing API (RingSquared-style routing decisions, availability stats, simple
/// yes/no) — docs/design/parallel-queuing.md. Stateless apart from reads; logging of requests is the
/// endpoint's job.
/// </summary>
public class ExternalRoutingService(IAgentStateStore stateStore, ITelephonyCallSessionStore sessionStore)
{
    public const string NoAgentsAvailable = "No Agents Available";

    /// <param name="groupId">Ask about one agent group only (e.g. Elite): its allowed members, and
    /// the calls queued restricted to it. Without it: every agent with a route to the campaign, and
    /// the campaign's unrestricted queued calls (a call pinned to a group doesn't compete for the
    /// general pool).</param>
    public async Task<CampaignAvailability> GetAvailabilityAsync(
        TenantDbContext db, Guid tenantId, Guid campaignId, Guid? groupId, CancellationToken ct = default)
    {
        var routes = await EligibleAgentRanker.GetBestRoutesAsync(db, campaignId, groupId, ct);

        var tiers = new Dictionary<int, (string? Label, int LoggedIn, int Available)>();
        int loggedIn = 0, available = 0;
        foreach (var (agentId, route) in routes)
        {
            var state = await stateStore.GetAsync(tenantId, agentId, ct);
            if (state is null || state.Code == AgentStateCodes.LoggedOut) continue;
            var isAvailable = state.Code == AgentStateCodes.Available;
            loggedIn++;
            if (isAvailable) available++;

            var t = tiers.TryGetValue(route.Tier, out var existing) ? existing : (Label: route.TierLabel, LoggedIn: 0, Available: 0);
            tiers[route.Tier] = (t.Label ?? route.TierLabel, t.LoggedIn + 1, t.Available + (isAvailable ? 1 : 0));
        }

        var now = DateTimeOffset.UtcNow;
        var queued = (await sessionStore.GetAllAsync(ct))
            .Where(s => s.CampaignId == campaignId && s.Vars.GetValueOrDefault("_queued") == "true"
                        && QueueOffer.RestrictGroupId(s.Vars) == groupId)
            .ToList();
        var longestWait = queued
            .Select(s => s.Vars.TryGetValue("_in_queue_at", out var iso) && DateTimeOffset.TryParse(iso, out var at)
                ? (int)Math.Max(0, (now - at).TotalSeconds) : 0)
            .DefaultIfEmpty(0).Max();

        return new CampaignAvailability(
            loggedIn, available, loggedIn - available, queued.Count, longestWait,
            tiers.OrderByDescending(t => t.Key)
                .Select(t => new TierAvailability(t.Key, t.Value.Label, t.Value.LoggedIn, t.Value.Available))
                .ToList());
    }

    /// <summary>TMS's Dial800Routing accept rule, per the campaign's accept mode. Every mode first
    /// needs an eligible agent logged in.</summary>
    public static RoutingDecision Decide(string acceptMode, int? limit, CampaignAvailability a)
    {
        if (a.LoggedIn == 0) return new RoutingDecision(false, NoAgentsAvailable);
        return acceptMode switch
        {
            ExternalRoutingAcceptMode.QueueWait => a.LongestWaitSeconds < (limit ?? 30)
                ? new RoutingDecision(true, $"Longest wait {a.LongestWaitSeconds}s under {limit ?? 30}s")
                : new RoutingDecision(false, NoAgentsAvailable),
            ExternalRoutingAcceptMode.AgentAvailable => a.Available > 0
                ? new RoutingDecision(true, "Agent available")
                : new RoutingDecision(false, NoAgentsAvailable),
            _ => a.Queued < (limit ?? 1)
                ? new RoutingDecision(true, $"{a.Queued} queued, under {limit ?? 1}")
                : new RoutingDecision(false, NoAgentsAvailable),
        };
    }

    /// <summary>Digits of a phone number as routers send it ("tel:+18005551234", "+1 800…"), NANP
    /// numbers reduced to their 10-digit national form — how TMS's Telephony table stored them and
    /// how the delivery targets are returned.</summary>
    public static string NormalizeNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
    }
}
