using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// The disposition catalog (S181, docs/dispositions-kpi-plan.md): reporting categories, scoped dispositions, and keeping
/// every interaction linked to the catalog entry its recorded text means.
/// </summary>
public interface IDispositionService
{
    /// <summary>All categories (the six built-ins are created on first use).</summary>
    Task<IReadOnlyList<DispositionCategory>> CategoriesAsync(CancellationToken ct = default);

    /// <summary>The active dispositions a call on <paramref name="campaignId"/> can record — narrowest scope wins per name.</summary>
    Task<IReadOnlyList<Disposition>> ForCampaignAsync(Guid? campaignId, CancellationToken ct = default);

    /// <summary>Re-reads each finished interaction's disposition on the call (its own field, else the record's, else what it
    /// completed with) and links it to the catalog. Called on completion and after every write to the disposition field.</summary>
    Task SyncCallAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Re-links every interaction to the catalog — after a disposition is created, renamed, aliased or re-scoped,
    /// so history follows the current catalog. Returns how many interactions changed.</summary>
    Task<int> RelinkAllAsync(CancellationToken ct = default);

    /// <summary>Recorded disposition texts that match nothing in the catalog, most frequent first.</summary>
    Task<IReadOnlyList<UnmappedDisposition>> UnmappedAsync(CancellationToken ct = default);
}

/// <param name="ProductionCount">Production interactions only (what KPIs see); <paramref name="Count"/> includes practice runs.</param>
public sealed record UnmappedDisposition(
    string Text, int Count, int ProductionCount, IReadOnlyList<Guid> CampaignIds, DateTimeOffset? LastSeen);
