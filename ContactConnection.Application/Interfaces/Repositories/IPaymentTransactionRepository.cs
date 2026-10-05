using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IPaymentTransactionRepository
{
    /// <summary>The call's most recent approved, not-yet-voided transaction — the implicit void
    /// target (see VoidPaymentNodeHandler: there's normally at most one live authorization per call,
    /// so no explicit node-reference config is needed).</summary>
    Task<PaymentTransaction?> GetMostRecentApprovedAsync(Guid callRecordId, CancellationToken ct = default);
    /// <summary>The interaction's most recent approved, un-voided authorization (S178 — a CS order on a transferred
    /// call must never touch the sales authorization). Null interaction = any on the call (legacy).</summary>
    Task<PaymentTransaction?> GetMostRecentApprovedAsync(Guid callRecordId, Guid? interactionId, CancellationToken ct = default);

    /// <summary>Every gateway call on the call, oldest first (call detail view).</summary>
    Task<IReadOnlyList<PaymentTransaction>> GetByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);

    Task AddAsync(PaymentTransaction transaction, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
