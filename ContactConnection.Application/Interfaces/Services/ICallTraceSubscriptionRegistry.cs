using ContactConnection.Application.Services;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Redis-backed registry of active live-trace subscriptions — shared across API instances
/// so filter matching and capture-cap enforcement are consistent regardless of which
/// instance handles a given call or step. A subscription only matches calls that start
/// after it was created (no backfill of in-progress calls).
/// </summary>
public interface ICallTraceSubscriptionRegistry
{
    Task<Guid> StartTraceAsync(StartTraceRequest request, CancellationToken ct = default);

    /// <summary>Called once, at telephony call start, with everything known about the new call.</summary>
    Task<IReadOnlyList<Guid>> MatchNewCallAsync(
        Guid tenantId, Guid? campaignId, Guid? flowId, string? dnis, string? ani, CancellationToken ct = default);

    /// <summary>
    /// Attaches a matched call to a subscription (spawns a tab client-side).
    /// Returns true if this attachment pushed the subscription's count cap to its limit.
    /// </summary>
    Task<bool> AttachCallAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default);

    /// <summary>Subscriptions currently watching a given call — used to route live steps.</summary>
    Task<IReadOnlyList<Guid>> GetSubscriptionsForCallAsync(Guid callRecordId, CancellationToken ct = default);

    Task StopTraceAsync(Guid subscriptionId, string reason, CancellationToken ct = default);

    Task MarkCallEndedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default);

    /// <summary>All active subscriptions for a tenant — used by the duration-cap expiry sweep.</summary>
    Task<IReadOnlyList<CallTraceSubscriptionInfo>> GetActiveSubscriptionsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>All tenant ids with at least one active subscription — lets the sweep avoid a full tenant scan.</summary>
    Task<IReadOnlyList<Guid>> GetActiveTenantsAsync(CancellationToken ct = default);
}
