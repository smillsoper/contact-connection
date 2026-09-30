namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Decides when a call's captured card (CallRecord.SensitiveData) is wiped, per the campaign's
/// CardDataRetention mode (S166). The Worker's retention sweep remains the time-based backstop.
/// </summary>
public interface ICardDataRetentionService
{
    /// <summary>True when the call's campaign keeps the card until the order is submitted — the
    /// script finishing or a Commit Point must then leave it in place.</summary>
    Task<bool> HoldsUntilOrderSubmittedAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>The order was submitted (an API Call node marked "releases card data" succeeded) —
    /// wipe the card now. No-op when nothing is on file.</summary>
    Task ReleaseAfterOrderSubmittedAsync(Guid callRecordId, CancellationToken ct = default);
}
