using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IOfferRepository
{
    Task<Offer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Offer>> GetByProductIdAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Offers for a product visible in a given client/campaign context — tenant-wide offers
    /// (ClientId null) plus this client's/campaign's own scoped offers. Unlike
    /// CustomFieldDefinition's scope resolution, this does NOT collapse to one "most specific"
    /// result — multiple offers are meant to coexist and be chosen from.
    /// </summary>
    Task<List<Offer>> GetAvailableForContextAsync(Guid productId, Guid? clientId, Guid? campaignId, CancellationToken ct = default);

    /// <summary>Returns all active offers for the current tenant, ordered by name.</summary>
    Task<List<Offer>> GetActiveAsync(CancellationToken ct = default);

    Task AddAsync(Offer offer, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
