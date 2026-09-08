using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICallStateHistoryRepository
{
    Task AddAsync(CallStateHistoryEntry entry, string tenantSchemaName, CancellationToken ct = default);

    /// <summary>
    /// Counts calls currently "live" (latest history row is not Completed/Abandoned), grouped by
    /// campaign and state. campaignIds null = every campaign in the tenant; otherwise restricted
    /// to that set. Used by the Call State by Campaign dashboard widget.
    /// </summary>
    Task<List<CampaignStateCount>> GetActiveStateCountsAsync(
        string tenantSchemaName, List<Guid>? campaignIds, CancellationToken ct = default);

    /// <summary>
    /// Every call in the tenant whose latest history row is non-terminal (not Completed/Abandoned),
    /// with the campaign carried on that latest row. Used by the startup orphaned-call
    /// reconciliation sweep to find dangling timelines to close.
    /// </summary>
    Task<List<NonTerminalCall>> GetNonTerminalCallsAsync(
        string tenantSchemaName, CancellationToken ct = default);

    /// <summary>
    /// The highest <c>sequence</c> recorded for a call, or 0 if it has no history rows. The
    /// reconciliation sweep uses this to append a terminal row at <c>max + 1</c> rather than
    /// trusting the recorder's Redis counter, which may have expired (24h TTL) for a call that
    /// has been dead long enough to need reconciling.
    /// </summary>
    Task<int> GetMaxSequenceAsync(
        string tenantSchemaName, Guid callRecordId, CancellationToken ct = default);
}

public record CampaignStateCount(Guid CampaignId, string State, int Count);

public record NonTerminalCall(Guid CallRecordId, Guid CampaignId);
