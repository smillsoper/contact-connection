using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IOrderNumberSequenceRepository
{
    Task<OrderNumberSequence?> GetByClientIdAsync(Guid clientId, CancellationToken ct = default);
    Task AddAsync(OrderNumberSequence sequence, CancellationToken ct = default);
    Task DeleteAsync(OrderNumberSequence sequence, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Atomically takes the client's next value and advances the sequence (a single
    /// UPDATE ... RETURNING — safe under concurrent calls). Returns the formatted order number, or
    /// null if the client has no sequence.</summary>
    Task<string?> AllocateAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Stamps <paramref name="orderNumber"/> onto the call record only if it doesn't
    /// already have one. Returns the call's order number afterward — the given one if this call
    /// won, or the one a concurrent first-use already wrote (the given number is then simply
    /// unused, leaving a gap in the sequence, which is harmless).</summary>
    Task<string?> AssignToCallRecordAsync(Guid callRecordId, string orderNumber, CancellationToken ct = default);
}
