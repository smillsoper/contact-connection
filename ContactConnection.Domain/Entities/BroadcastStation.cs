namespace ContactConnection.Domain.Entities;

/// <summary>
/// A licensed US broadcast station from the FCC's public LMS database (S171, Media Agency Phase B).
/// Platform-wide — the same for every tenant — and rebuilt daily by the Worker (FccStationSyncService).
/// Coordinates are the transmitter site from the station's most recent application with a location,
/// which is what nearest-station attribution measures the caller's distance to.
/// </summary>
public class BroadcastStation
{
    /// <summary>The FCC's facility id — stable across call-sign changes.</summary>
    public int FacilityId { get; set; }
    public string CallSign { get; set; } = "";
    /// <summary>FCC service code: DTV (full-power TV), LPD/LPT (low power), DCA (Class A), FM, AM, FX (translator)…</summary>
    public string ServiceCode { get; set; } = "";
    public string? CommunityCity { get; set; }
    public string? CommunityState { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? NetworkAffiliation { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
