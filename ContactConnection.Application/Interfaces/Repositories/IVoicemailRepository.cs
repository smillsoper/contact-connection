using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Tenant-scoped persistence for <see cref="Voicemail"/> rows (resolves the schema from the
/// ambient <c>TenantContext</c>, like <see cref="IScreenRecordingRepository"/>). The ESL
/// background path writes voicemails through <c>ITenantDbContextFactory</c> directly instead,
/// where the schema is passed explicitly — this interface serves the API surface.
/// </summary>
public interface IVoicemailRepository
{
    Task<Voicemail?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Voicemail>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Campaign voicemail inbox. <paramref name="status"/> null = all; newest first.</summary>
    Task<IReadOnlyList<Voicemail>> ListByCampaignAsync(
        Guid campaignId, string? status, int limit, CancellationToken ct = default);

    Task AddAsync(Voicemail voicemail, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
