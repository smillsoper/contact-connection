using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Tenant-scoped persistence for <see cref="Callback"/> rows (resolves the schema from the
/// ambient <c>TenantContext</c>, like <see cref="IVoicemailRepository"/>). The worker's
/// <c>CallbackProcessingService</c> writes callbacks through <c>ITenantDbContextFactory</c>
/// directly instead, where the schema is passed explicitly — this interface serves the API
/// surface.
/// </summary>
public interface ICallbackRepository
{
    Task<Callback?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Callback>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Campaign callback list. <paramref name="status"/> null = all; newest request first.</summary>
    Task<IReadOnlyList<Callback>> ListByCampaignAsync(
        Guid campaignId, string? status, int limit, CancellationToken ct = default);

    /// <summary>Non-terminal callbacks for this number — used to cancel a pending callback when
    /// the caller phones back in.</summary>
    Task<IReadOnlyList<Callback>> ListPendingByNumberAsync(
        Guid campaignId, string callbackNumber, CancellationToken ct = default);

    Task AddAsync(Callback callback, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
