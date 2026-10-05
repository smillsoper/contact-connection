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
    /// <summary>Same conditional write for an interaction's order number (S178, interaction-scoped commerce).</summary>
    Task<string?> AssignToInteractionAsync(Guid interactionId, string orderNumber, CancellationToken ct = default);
}
