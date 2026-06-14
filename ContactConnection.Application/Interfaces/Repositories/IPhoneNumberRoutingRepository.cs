using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IPhoneNumberRoutingRepository
{
    Task<PhoneNumberRouting?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task UpsertAsync(string number, Guid tenantId, Guid campaignId, bool isActive = true, CancellationToken ct = default);
    Task SetActiveAsync(string number, bool isActive, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
