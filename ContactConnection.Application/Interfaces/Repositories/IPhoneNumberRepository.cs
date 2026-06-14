using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IPhoneNumberRepository
{
    Task<PhoneNumber?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PhoneNumber?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task<List<PhoneNumber>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default);
    Task AddAsync(PhoneNumber phoneNumber, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
