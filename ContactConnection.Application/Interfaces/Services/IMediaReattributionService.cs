using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Re-attributes a Local media call to the station nearest the caller once the script captures their
/// zip (S171, Media Agency Phase B) — at call arrival only the phone number's area code was known.
/// </summary>
public interface IMediaReattributionService
{
    /// <summary>
    /// The call's attribution re-resolved with <paramref name="zip"/> as the caller's location, or null
    /// when nothing should change: no Local attribution, not a 5-digit zip, or already placed by that zip.
    /// The newest zip always wins; one missing from the zip table falls back to the caller's area code,
    /// then the default Local assignment. The caller sets it on the record and saves.
    /// </summary>
    Task<MediaAttribution?> ForZipAsync(CallRecord record, string? zip, CancellationToken ct = default);

    /// <summary>
    /// Loads the call record, re-attributes it by <paramref name="zip"/> (as <see cref="ForZipAsync"/>)
    /// and saves it. Returns the new attribution, or null when nothing changed. Used by flow nodes that
    /// capture a zip outside the address node (an input node with a ZIP mask).
    /// </summary>
    Task<MediaAttribution?> ApplyZipAsync(Guid callRecordId, string? zip, CancellationToken ct = default);
}
