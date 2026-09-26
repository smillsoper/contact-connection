using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IPaymentTransactionRepository
{
    /// <summary>The call's most recent approved, not-yet-voided transaction — the implicit void
    /// target (see VoidPaymentNodeHandler: there's normally at most one live authorization per call,
    /// so no explicit node-reference config is needed).</summary>
    Task<PaymentTransaction?> GetMostRecentApprovedAsync(Guid callRecordId, CancellationToken ct = default);

    Task AddAsync(PaymentTransaction transaction, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
