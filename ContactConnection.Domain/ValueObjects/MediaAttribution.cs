using ContactConnection.Domain.Entities;

namespace ContactConnection.Domain.ValueObjects;

/// <summary>
/// The media buy a call is attributed to — a COPY of the assignment in effect for the dialed number on
/// the call's date (S171, Media Agency Phase A). Stored on call_records.media_attribution (JSONB) so the
/// call stays pinned to it however the assignment is edited later. <see cref="AssignmentId"/> is kept
/// for traceability only — nothing reads the assignment through it.
/// </summary>
public record MediaAttribution(
    Guid AssignmentId,
    string MarketType,
    string Agency,
    string Station,
    string? MediaType,
    string? AdType,
    DateOnly StartDate,
    string? PhoneNumber,
    Dictionary<string, string> Fields)
{
    public static MediaAttribution From(MediaAssignment a, string agencyName, string? phoneNumber) => new(
        a.Id, a.MarketType, agencyName, a.Station, a.MediaType, a.AdType, a.StartDate, phoneNumber,
        new Dictionary<string, string>(a.FieldValues));
}
