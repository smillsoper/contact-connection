using System.Globalization;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.CallTrace;

/// <summary>
/// Redis-backed so subscription state and call→subscription matching are consistent across
/// API instances (a SignalR Redis backplane handles cross-instance message delivery separately —
/// see CallTraceHub registration in Program.cs). Not an in-memory dictionary.
/// </summary>
public class RedisCallTraceSubscriptionRegistry(IConnectionMultiplexer redis) : ICallTraceSubscriptionRegistry
{
    private const string StatusActive = "active";
    private const string StatusStopped = "stopped";

    private readonly IDatabase _db = redis.GetDatabase();

    private static string SubKey(Guid subscriptionId) => $"calltrace:sub:{subscriptionId}";
    private static string ActiveTenantSubsKey(Guid tenantId) => $"calltrace:active:{tenantId}";
    private static string ActiveTenantsKey() => "calltrace:activetenants";
    private static string CallSubsKey(Guid callRecordId) => $"calltrace:call:{callRecordId}";

    public async Task<Guid> StartTraceAsync(StartTraceRequest request, CancellationToken ct = default)
    {
        var subscriptionId = Guid.NewGuid();

        var captureValue = request.CaptureMode == CallTraceCaptureMode.Duration
            ? Math.Clamp(request.CaptureValue, 1, (int)CallTraceLimits.MaxCaptureDuration.TotalMinutes)
            : Math.Clamp(request.CaptureValue, 1, CallTraceLimits.MaxCaptureCount);

        var entries = new List<HashEntry>
        {
            new("TenantId", request.TenantId.ToString()),
            new("CampaignId", request.CampaignId?.ToString() ?? ""),
            new("FlowId", request.FlowId?.ToString() ?? ""),
            new("Dnis", request.Dnis ?? ""),
            new("Ani", request.Ani ?? ""),
            new("CaptureMode", request.CaptureMode),
            new("CaptureValue", captureValue.ToString(CultureInfo.InvariantCulture)),
            new("MatchedCount", "0"),
            new("StartedAt", DateTimeOffset.UtcNow.ToString("O")),
            new("Status", StatusActive),
        };

        var subKey = SubKey(subscriptionId);
        await _db.HashSetAsync(subKey, entries.ToArray());
        // Hard backstop TTL — even a count-mode trace that never matches a call self-cleans.
        await _db.KeyExpireAsync(subKey, CallTraceLimits.MaxCaptureDuration + TimeSpan.FromMinutes(5));

        await _db.SetAddAsync(ActiveTenantSubsKey(request.TenantId), subscriptionId.ToString());
        await _db.SetAddAsync(ActiveTenantsKey(), request.TenantId.ToString());

        return subscriptionId;
    }

    public async Task<IReadOnlyList<Guid>> MatchNewCallAsync(
        Guid tenantId, Guid? campaignId, Guid? flowId, string? dnis, string? ani, CancellationToken ct = default)
    {
        var candidateIds = await _db.SetMembersAsync(ActiveTenantSubsKey(tenantId));
        var matches = new List<Guid>();

        foreach (var candidate in candidateIds)
        {
            if (!Guid.TryParse(candidate.ToString(), out var subscriptionId)) continue;

            var hash = await _db.HashGetAllAsync(SubKey(subscriptionId));
            if (hash.Length == 0) continue; // expired

            var fields = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
            if (fields.GetValueOrDefault("Status") != StatusActive) continue;

            if (!FilterMatches(fields, campaignId, flowId, dnis, ani)) continue;

            matches.Add(subscriptionId);
        }

        return matches;
    }

    private static bool FilterMatches(
        Dictionary<string, string> fields, Guid? campaignId, Guid? flowId, string? dnis, string? ani)
    {
        var subCampaignId = fields.GetValueOrDefault("CampaignId");
        if (!string.IsNullOrEmpty(subCampaignId) &&
            (!campaignId.HasValue || subCampaignId != campaignId.Value.ToString()))
            return false;

        var subFlowId = fields.GetValueOrDefault("FlowId");
        if (!string.IsNullOrEmpty(subFlowId) &&
            (!flowId.HasValue || subFlowId != flowId.Value.ToString()))
            return false;

        var subDnis = fields.GetValueOrDefault("Dnis");
        if (!string.IsNullOrEmpty(subDnis) && subDnis != dnis)
            return false;

        var subAni = fields.GetValueOrDefault("Ani");
        if (!string.IsNullOrEmpty(subAni) && subAni != ani)
            return false;

        return true;
    }

    public async Task<bool> AttachCallAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default)
    {
        var subKey = SubKey(subscriptionId);
        var matchedCount = await _db.HashIncrementAsync(subKey, "MatchedCount");

        await _db.SetAddAsync(CallSubsKey(callRecordId), subscriptionId.ToString());
        await _db.KeyExpireAsync(CallSubsKey(callRecordId), TimeSpan.FromHours(24));

        var hash = await _db.HashGetAllAsync(subKey);
        var fields = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
        var captureMode = fields.GetValueOrDefault("CaptureMode");
        var captureValue = int.TryParse(fields.GetValueOrDefault("CaptureValue"), out var cv) ? cv : 0;

        return (captureMode == CallTraceCaptureMode.Count && matchedCount >= captureValue)
            || matchedCount >= CallTraceLimits.MaxCaptureCount;
    }

    /// <summary>
    /// Deliberately does NOT filter by subscription Status. A subscription that just hit its
    /// capture cap is marked "stopped" so it stops matching NEW calls, but the calls it already
    /// captured (including the one that triggered the cap) must keep streaming steps until they
    /// end — otherwise the very last call captured would show zero steps. Membership in
    /// calltrace:call:{callRecordId} (set at match time, cleared at call-end) is what actually
    /// governs step routing.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetSubscriptionsForCallAsync(Guid callRecordId, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(CallSubsKey(callRecordId));
        var result = new List<Guid>();

        foreach (var member in members)
            if (Guid.TryParse(member.ToString(), out var subscriptionId))
                result.Add(subscriptionId);

        return result;
    }

    public async Task StopTraceAsync(Guid subscriptionId, string reason, CancellationToken ct = default)
    {
        var subKey = SubKey(subscriptionId);
        var tenantIdStr = await _db.HashGetAsync(subKey, "TenantId");
        await _db.HashSetAsync(subKey, "Status", StatusStopped);

        if (tenantIdStr.HasValue && Guid.TryParse(tenantIdStr.ToString(), out var tenantId))
            await _db.SetRemoveAsync(ActiveTenantSubsKey(tenantId), subscriptionId.ToString());
    }

    public async Task MarkCallEndedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default) =>
        await _db.SetRemoveAsync(CallSubsKey(callRecordId), subscriptionId.ToString());

    public async Task<IReadOnlyList<CallTraceSubscriptionInfo>> GetActiveSubscriptionsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var ids = await _db.SetMembersAsync(ActiveTenantSubsKey(tenantId));
        var result = new List<CallTraceSubscriptionInfo>();

        foreach (var id in ids)
        {
            if (!Guid.TryParse(id.ToString(), out var subscriptionId)) continue;

            var hash = await _db.HashGetAllAsync(SubKey(subscriptionId));
            if (hash.Length == 0) continue;

            var fields = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
            if (fields.GetValueOrDefault("Status") != StatusActive) continue;

            result.Add(new CallTraceSubscriptionInfo
            {
                SubscriptionId = subscriptionId,
                TenantId = tenantId,
                CampaignId = ParseNullableGuid(fields.GetValueOrDefault("CampaignId")),
                FlowId = ParseNullableGuid(fields.GetValueOrDefault("FlowId")),
                Dnis = string.IsNullOrEmpty(fields.GetValueOrDefault("Dnis")) ? null : fields["Dnis"],
                Ani = string.IsNullOrEmpty(fields.GetValueOrDefault("Ani")) ? null : fields["Ani"],
                CaptureMode = fields.GetValueOrDefault("CaptureMode") ?? CallTraceCaptureMode.Count,
                CaptureValue = int.TryParse(fields.GetValueOrDefault("CaptureValue"), out var cv) ? cv : 0,
                MatchedCount = int.TryParse(fields.GetValueOrDefault("MatchedCount"), out var mc) ? mc : 0,
                StartedAt = DateTimeOffset.TryParse(fields.GetValueOrDefault("StartedAt"), out var started)
                    ? started : DateTimeOffset.UtcNow,
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<Guid>> GetActiveTenantsAsync(CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(ActiveTenantsKey());
        return members
            .Select(m => Guid.TryParse(m.ToString(), out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }

    private static Guid? ParseNullableGuid(string? value) =>
        !string.IsNullOrEmpty(value) && Guid.TryParse(value, out var id) ? id : null;
}
