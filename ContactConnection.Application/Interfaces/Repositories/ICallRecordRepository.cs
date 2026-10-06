using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICallRecordRepository
{
    Task<CallRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CallRecord?> GetByIdWithInteractionsAsync(Guid id, CancellationToken ct = default);
    Task<CallRecord?> GetByContactIdExternalAsync(string contactIdExternal, CancellationToken ct = default);
    /// <summary>Call Records admin list — newest first, filtered; see CallRecordSearchCriteria.</summary>
    Task<CallRecordSearchPage> SearchAsync(CallRecordSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Which of these calls have a flow API call whose last result was a failure (an
    /// api_call node's <c>{output}.success</c> = "false" in a session's variables) — e.g. an order
    /// the Order API rejected. Clears once a re-run succeeds.</summary>
    Task<IReadOnlySet<Guid>> FindWithFailedApiCallsAsync(IReadOnlyCollection<Guid> callRecordIds, CancellationToken ct = default);

    Task AddAsync(CallRecord record, CancellationToken ct = default);
    /// <summary>Track a new interaction explicitly. Found only through the record's collection, EF treats an entity
    /// with a pre-set Guid key as an existing row (UPDATE → concurrency exception).</summary>
    Task AddInteractionAsync(CallInteraction interaction, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Ids of call records that still hold a recording (<c>RecordingRetained</c> and a start
    /// timestamp), oldest call first — the retention purge sweep pulls a batch and applies each
    /// record's campaign-specific <c>RecordingRetentionDays</c> in memory before deleting.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindRetainedRecordingIdsOldestFirstAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Ids of call records still holding a PCI <c>SensitiveData</c> blob, oldest
    /// <c>SensitiveDataStoredAt</c> first — the Worker's sensitive-data retention sweep pulls a
    /// batch and wipes anything past the configured TTL.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindWithSensitiveDataOldestFirstAsync(int limit, CancellationToken ct = default);
}

/// <summary>Filters for the Call Records admin list. Phone matches the caller ID or either contact
/// phone (digits); Name matches first/last name (contains, case-insensitive).</summary>
public record CallRecordSearchCriteria(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? CampaignId = null,
    string? Phone = null,
    string? OrderNumber = null,
    string? Name = null,
    bool FailedApiCallsOnly = false,
    int Skip = 0,
    int Take = 50,
    /// <summary>S179 launch modes: production (the default — live calls only), training, sandbox, or "all".</summary>
    string? RunMode = null,
    /// <summary>S181: calls where any interaction recorded this catalog disposition / a disposition in this reporting
    /// category / text matching nothing in the catalog.</summary>
    Guid? DispositionId = null,
    Guid? DispositionCategoryId = null,
    bool UnmappedDispositionOnly = false);

public record CallRecordSearchPage(IReadOnlyList<CallRecord> Items, int Total);

