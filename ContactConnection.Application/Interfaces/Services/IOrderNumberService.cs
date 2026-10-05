using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Hands out a call's order number from its client's OrderNumberSequence. Lazy and idempotent:
/// the first caller that needs a number (payment authorization, order submission) allocates it;
/// every later caller on the same call gets the same number back. See CallRecord.OrderNumber.
/// </summary>
public interface IOrderNumberService
{
    /// <summary>The interaction's order number (S178): each interaction's order has its own. The call's first interaction
    /// keeps the record's number (mirrored onto the record until readers move to the interaction). Null interaction =
    /// the record-level behaviour.</summary>
    Task<string?> GetOrAssignAsync(CallRecord record, CallInteraction? interaction, CancellationToken ct = default);
}
