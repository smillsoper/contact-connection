using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Startup sweep that finalizes calls a previous process left non-terminal (hard restart, crash,
/// dev Ctrl-C). See <see cref="IOrphanedCallReconciler"/>. Mirrors the manual cleanup done by
/// hand in Sessions 118 and 119, but automatic and safe to run every boot:
///   1. <c>call_records</c> with no <c>call_end_at</c> older than the grace window → <c>Complete()</c>.
///   2. <c>call_state_history</c> timelines whose latest row is non-terminal → append a terminal row.
/// Both steps skip any call that still has a live Redis telephony session.
///
/// The terminal state row is written straight through the repository at <c>maxSequence + 1</c>
/// (not via <see cref="ICallStateHistoryRecorder"/>) because the recorder's Redis sequence
/// counter has a 24h TTL — a call dead long enough to need reconciling may have lost it, and a
/// restarted-from-1 sequence would sort behind the dangling row, leaving the phantom in place.
/// </summary>
public class OrphanedCallReconciler : IOrphanedCallReconciler
{
    private const string ReconcileDetail = "Reconciled on API startup — no live telephony session";

    private readonly ContactConnectionDbContext _platformDb;
    private readonly ITenantDbContextFactory _dbFactory;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ICallStateHistoryRepository _stateHistory;
    private readonly IDashboardNotifier _dashboardNotifier;
    private readonly ILogger<OrphanedCallReconciler> _logger;
    private readonly TimeSpan _minAge;

    public OrphanedCallReconciler(
        ContactConnectionDbContext platformDb,
        ITenantDbContextFactory dbFactory,
        ITelephonyCallSessionStore sessionStore,
        ICallStateHistoryRepository stateHistory,
        IDashboardNotifier dashboardNotifier,
        IConfiguration config,
        ILogger<OrphanedCallReconciler> logger)
    {
        _platformDb        = platformDb;
        _dbFactory         = dbFactory;
        _sessionStore      = sessionStore;
        _stateHistory      = stateHistory;
        _dashboardNotifier = dashboardNotifier;
        _logger            = logger;

        var minAgeMinutes = config.GetValue("Telephony:OrphanReconciliation:MinAgeMinutes", 15);
        _minAge = TimeSpan.FromMinutes(Math.Max(0, minAgeMinutes));
    }

    public async Task<OrphanedCallReconciliationResult> RunAsync(CancellationToken ct = default)
    {
        // Live calls = anything still tracked in Redis (4-hour session TTL). Channel UUIDs are
        // globally unique, so one flat set is valid across every tenant.
        var liveSessions     = await _sessionStore.GetAllAsync(ct);
        var liveChannelUuids = liveSessions.Select(s => s.ChannelUuid).ToHashSet(StringComparer.Ordinal);
        var liveRecordIds    = liveSessions.Select(s => s.CallRecordId).ToHashSet();

        var cutoff  = DateTimeOffset.UtcNow - _minAge;
        var tenants = await _platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        var recordsClosed   = 0;
        var timelinesClosed = 0;

        foreach (var tenant in tenants)
        {
            try
            {
                var (r, s) = await ReconcileTenantAsync(tenant, liveChannelUuids, liveRecordIds, cutoff, ct);
                recordsClosed   += r;
                timelinesClosed += s;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Orphaned-call reconciliation failed for tenant {Tenant} — continuing with the rest",
                    tenant.Subdomain);
            }
        }

        return new OrphanedCallReconciliationResult(tenants.Count, recordsClosed, timelinesClosed);
    }

    private async Task<(int RecordsClosed, int TimelinesClosed)> ReconcileTenantAsync(
        Tenant tenant,
        HashSet<string> liveChannelUuids,
        HashSet<Guid> liveRecordIds,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        await using var db = _dbFactory.Create(tenant.SchemaName);

        bool IsLive(Guid recordId, string? contactId) =>
            liveRecordIds.Contains(recordId) ||
            (contactId is not null && liveChannelUuids.Contains(contactId));

        // ── 1. call_records still open (no call_end_at) past the grace window ─────
        var openRecords = await db.CallRecords
            .Where(r => r.CallEndAt == null && r.CreatedAt < cutoff)
            .ToListAsync(ct);

        var recordsClosed = 0;
        foreach (var record in openRecords)
        {
            if (IsLive(record.Id, record.ContactIdExternal)) continue;
            record.Complete();
            recordsClosed++;
        }
        if (recordsClosed > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Orphaned-call reconciliation: closed {Count} stranded call record(s) for tenant {Tenant}",
                recordsClosed, tenant.Subdomain);
        }

        // ── 2. call_state_history timelines whose latest row is non-terminal ─────
        var nonTerminal     = await _stateHistory.GetNonTerminalCallsAsync(tenant.SchemaName, ct);
        var timelinesClosed = 0;
        foreach (var call in nonTerminal)
        {
            if (liveRecordIds.Contains(call.CallRecordId)) continue;

            var record = openRecords.FirstOrDefault(r => r.Id == call.CallRecordId)
                         ?? await db.CallRecords.FirstOrDefaultAsync(r => r.Id == call.CallRecordId, ct);
            if (record is null) continue;                 // history row for a vanished record
            if (record.CreatedAt >= cutoff) continue;     // still inside the grace window
            if (IsLive(record.Id, record.ContactIdExternal)) continue;

            var nextSequence = await _stateHistory.GetMaxSequenceAsync(tenant.SchemaName, call.CallRecordId, ct) + 1;
            var entry = CallStateHistoryEntry.Create(
                tenant.Id, call.CallRecordId, nextSequence, CallHistoryState.Completed,
                call.CampaignId, agentId: null, detail: ReconcileDetail,
                abandonType: null, abandonLength: null);

            await _stateHistory.AddAsync(entry, tenant.SchemaName, ct);
            await _dashboardNotifier.NotifyCallStateChangedAsync(
                tenant.Id, call.CampaignId, CallHistoryState.Completed, ct);
            timelinesClosed++;
        }
        if (timelinesClosed > 0)
            _logger.LogWarning(
                "Orphaned-call reconciliation: closed {Count} dangling state timeline(s) for tenant {Tenant}",
                timelinesClosed, tenant.Subdomain);

        return (recordsClosed, timelinesClosed);
    }
}
