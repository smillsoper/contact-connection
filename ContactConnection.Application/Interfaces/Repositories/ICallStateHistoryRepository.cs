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
}

public record CampaignStateCount(Guid CampaignId, string State, int Count);
