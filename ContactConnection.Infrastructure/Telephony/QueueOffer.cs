namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Session-var plumbing for parallel-queuing offers (docs/design/parallel-queuing.md), shared by
/// RouteToQueueNodeHandler (writes them at queue entry) and the Api's QueuePollingService /
/// EslBackgroundService / QueuedCallDeliveryService (read them).
///   _restrict_group_id     — "only offer to this agent group" (e.g. Elite), no fallback
///   _eligible_tier_labels  — "agentId=label,…" for the initial screen pop's tier badge
/// </summary>
public static class QueueOffer
{
    public const string RestrictGroupVar = "_restrict_group_id";

    /// <summary>Redis key holding a queued call's current offer, "tier|labels|heldForTier|agentCount|noneLoggedIn"
    /// (empty tier = nobody offered; noneLoggedIn "1" = nobody who could ever take it is signed in —
    /// the supervisor alert) — written by QueuePollingService, read by the Queued Calls widget.</summary>
    public static string OfferTierKey(string channelUuid) => $"queue_offer_tier:{channelUuid}";

    public static string FormatOfferTier(OfferSet offer, bool noneLoggedIn = false) =>
        $"{offer.OfferTier}|{string.Join("/", offer.Agents.Select(a => a.TierLabel).Where(l => !string.IsNullOrEmpty(l)).Distinct())}" +
        $"|{offer.HeldForTier}|{offer.Agents.Count}|{(noneLoggedIn ? "1" : "")}";

    public static (int? Tier, string? Labels, int? HeldForTier, int AgentCount, bool NoneLoggedIn) ParseOfferTier(string? raw)
    {
        var parts = (raw ?? "").Split('|');
        int? Int(int i) => parts.Length > i && int.TryParse(parts[i], out var v) ? v : null;
        return (Int(0), parts.Length > 1 && parts[1] != "" ? parts[1] : null, Int(2), Int(3) ?? 0,
            parts.Length > 4 && parts[4] == "1");
    }

    public static Guid? RestrictGroupId(IReadOnlyDictionary<string, string> vars) =>
        vars.TryGetValue(RestrictGroupVar, out var raw) && Guid.TryParse(raw, out var id) ? id : null;

    public static string FormatLabels(IEnumerable<RankedAgent> agents) =>
        string.Join(",", agents.Where(a => !string.IsNullOrEmpty(a.TierLabel))
            .Select(a => $"{a.AgentId}={a.TierLabel!.Replace(",", " ").Replace("=", " ")}"));

    public static Dictionary<Guid, string> ParseLabels(string? raw)
    {
        var labels = new Dictionary<Guid, string>();
        if (string.IsNullOrEmpty(raw)) return labels;
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && Guid.TryParse(pair[..eq], out var id)) labels[id] = pair[(eq + 1)..];
        }
        return labels;
    }

    public static HashSet<Guid> ParseAgentIds(string? csv)
    {
        var ids = new HashSet<Guid>();
        if (string.IsNullOrEmpty(csv)) return ids;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Guid.TryParse(part, out var id)) ids.Add(id);
        return ids;
    }
}
