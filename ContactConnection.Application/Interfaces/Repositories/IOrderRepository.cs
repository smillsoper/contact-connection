using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> GetByCallRecordIdAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Finds the order that contains a given OrderLine id. Used by inbound fulfillment
    /// webhooks, which identify a shipment by our OrderLine.Id (given to the vendor as their
    /// reference at submit time) rather than by our Order.Id.</summary>
    Task<Order?> GetByLineIdAsync(Guid lineId, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
