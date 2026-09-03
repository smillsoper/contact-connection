using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Tenant-scoped persistence for <see cref="ScheduledCallback"/> rows (resolves the schema from
/// the ambient <c>TenantContext</c>, like <see cref="IVoicemailRepository"/>). The Worker's
/// <c>ScheduledCallbackProcessingService</c> writes rows through <c>ITenantDbContextFactory</c>
/// directly instead, where the schema is passed explicitly — this interface serves the API
/// surface.
/// </summary>
public interface IScheduledCallbackRepository
{
    Task<ScheduledCallback?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledCallback>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Campaign list. <paramref name="status"/> null = all; newest request first.</summary>
    Task<IReadOnlyList<ScheduledCallback>> ListByCampaignAsync(
        Guid campaignId, string? status, int limit, CancellationToken ct = default);

    /// <summary>Non-terminal rows for this number — used to cancel a pending callback when the
    /// caller reaches an agent another way.</summary>
    Task<IReadOnlyList<ScheduledCallback>> ListPendingByNumberAsync(
        Guid campaignId, string callbackNumber, CancellationToken ct = default);

    Task AddAsync(ScheduledCallback callback, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
