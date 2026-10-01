namespace ContactConnection.Domain.Entities;

/// <summary>
/// Which media buy a phone number is attributed to from a given date (S171, Media Agency Phase A).
/// One row per assignment; the rows for a number ARE its history.
///
/// <list type="bullet">
/// <item><b>National</b> — exactly one in effect per number at a time. Adding a new one with a start
/// date ends the previous one the day before (see <see cref="MediaAssignmentRules"/>).</item>
/// <item><b>Local</b> — several can be in effect at once, one per station. The call goes to the one whose
/// FCC station is closest to the caller (the zip the script captures, else the phone number's area
/// code); with no caller location, or no located stations, the one marked <see cref="IsDefaultLocal"/>. Markets are deliberately not tracked: mapping
/// stations to market areas is the media agency's job for their reporting — ours is attributing the call
/// to the right station (Stephen, S171).</item>
/// </list>
///
/// Never referenced from a call: at call time the assignment in effect is COPIED onto the call record
/// (<see cref="ValueObjects.MediaAttribution"/>), so later edits can't change what a past call was
/// attributed to.
/// </summary>
public class MediaAssignment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PhoneNumberId { get; private set; }
    public string MarketType { get; private set; } = MediaMarketType.National;
    public Guid MediaAgencyId { get; private set; }
    public string Station { get; private set; } = "";
    /// <summary>The FCC facility the station was picked from (Phase B, S171); null for free-text
    /// stations (print, digital, cable networks…). Its transmitter coordinates are copied here so
    /// nearest-station attribution doesn't depend on the platform station table later changing.</summary>
    public int? StationFacilityId { get; private set; }
    public double? StationLatitude { get; private set; }
    public double? StationLongitude { get; private set; }
    public string? MediaType { get; private set; }   // TV, Radio, Print, …
    public string? AdType { get; private set; }      // SF, LF, MF, PI, Paid, …
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }   // null = open-ended
    public bool IsDefaultLocal { get; private set; }
    /// <summary>Values for the agency's fields, keyed by the agency's field name.</summary>
    public Dictionary<string, string> FieldValues { get; private set; } = [];
    public string? CreatedByName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MediaAssignment() { }

    public static MediaAssignment Create(
        Guid tenantId, Guid phoneNumberId, string marketType, Guid mediaAgencyId, string station,
        DateOnly startDate, string? createdByName = null)
    {
        if (!MediaMarketType.IsValid(marketType)) throw new ArgumentException($"Unknown market type '{marketType}'.", nameof(marketType));
        var now = DateTimeOffset.UtcNow;
        var a = new MediaAssignment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PhoneNumberId = phoneNumberId, MarketType = marketType,
            MediaAgencyId = mediaAgencyId, StartDate = startDate, CreatedByName = createdByName,
            CreatedAt = now, UpdatedAt = now,
        };
        a.SetStation(station);
        return a;
    }

    public void SetStation(string station)
    {
        if (string.IsNullOrWhiteSpace(station)) throw new ArgumentException("Station is required.", nameof(station));
        Station = station.Trim();
        StationFacilityId = null;
        StationLatitude = StationLongitude = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A station picked from the FCC list — the name plus its facility id and location.</summary>
    public void SetStation(string station, int facilityId, double latitude, double longitude)
    {
        SetStation(station);
        StationFacilityId = facilityId;
        StationLatitude = latitude;
        StationLongitude = longitude;
    }

    public void SetDetails(string? mediaType, string? adType)
    {
        MediaType = Blank(mediaType);
        AdType    = Blank(adType);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetFieldValues(IDictionary<string, string> values)
    {
        FieldValues = values
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value?.Trim() ?? "");
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEndDate(DateOnly? endDate)
    {
        if (endDate is { } e && e < StartDate)
            throw new ArgumentException("An assignment can't end before it starts.", nameof(endDate));
        EndDate = endDate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetDefaultLocal(bool isDefault)
    {
        IsDefaultLocal = MarketType == MediaMarketType.Local && isDefault;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>In effect on <paramref name="date"/>: started on or before it, not ended before it.</summary>
    public bool InEffectOn(DateOnly date) => StartDate <= date && (EndDate is null || EndDate >= date);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class MediaMarketType
{
    public const string National = "national";
    public const string Local = "local";
    public static bool IsValid(string? v) => v is National or Local;
}

/// <summary>Rules spanning a number's assignments (S171). Pure — callers load and save.</summary>
public static class MediaAssignmentRules
{
    /// <summary>
    /// Fits a new National assignment into the number's National history without overlaps: the one in
    /// effect on its start date ends the day before; if a later one is already scheduled, the new one
    /// ends the day before that. Returns the assignments whose end date changed.
    /// </summary>
    public static List<MediaAssignment> FitNational(MediaAssignment added, IEnumerable<MediaAssignment> existing)
    {
        var changed = new List<MediaAssignment>();
        var nationals = existing.Where(a => a.MarketType == MediaMarketType.National && a.Id != added.Id).ToList();

        if (nationals.Any(a => a.StartDate == added.StartDate))
            throw new InvalidOperationException("Another National assignment already starts on that date — edit it instead.");

        var current = nationals.FirstOrDefault(a => a.InEffectOn(added.StartDate));
        if (current is not null)
        {
            current.SetEndDate(added.StartDate.AddDays(-1));
            changed.Add(current);
        }

        var next = nationals.Where(a => a.StartDate > added.StartDate).OrderBy(a => a.StartDate).FirstOrDefault();
        if (next is not null) added.SetEndDate(next.StartDate.AddDays(-1));
        return changed;
    }

    /// <summary>Makes <paramref name="chosen"/> the number's only default Local assignment.</summary>
    public static List<MediaAssignment> MakeDefaultLocal(MediaAssignment chosen, IEnumerable<MediaAssignment> existing)
    {
        var changed = new List<MediaAssignment>();
        foreach (var other in existing.Where(a => a.Id != chosen.Id && a.IsDefaultLocal))
        {
            other.SetDefaultLocal(false);
            changed.Add(other);
        }
        chosen.SetDefaultLocal(true);
        return changed;
    }

    /// <summary>
    /// The assignment a call on <paramref name="date"/> is attributed to: the National one in effect;
    /// else, when the caller's location is known, the in-effect Local one whose FCC station is nearest
    /// (Phase B); else the default Local one in effect; else the most recently started Local one.
    /// </summary>
    public static MediaAssignment? Resolve(IEnumerable<MediaAssignment> assignments, DateOnly date, ValueObjects.GeoPoint? caller = null)
    {
        var inEffect = assignments.Where(a => a.InEffectOn(date)).ToList();
        var national = inEffect.FirstOrDefault(a => a.MarketType == MediaMarketType.National);
        if (national is not null) return national;

        var locals = inEffect.Where(a => a.MarketType == MediaMarketType.Local).ToList();
        if (caller is { } point && Nearest(locals, point) is { } nearest) return nearest.Assignment;
        return locals.OrderByDescending(a => a.IsDefaultLocal).ThenByDescending(a => a.StartDate).FirstOrDefault();
    }

    /// <summary>The located assignment (picked from the FCC list) nearest <paramref name="caller"/>.</summary>
    public static (MediaAssignment Assignment, double Miles)? Nearest(IEnumerable<MediaAssignment> assignments, ValueObjects.GeoPoint caller) =>
        assignments
            .Where(a => a.StationLatitude is not null && a.StationLongitude is not null)
            .Select(a => (Assignment: a, Miles: caller.MilesTo(new(a.StationLatitude!.Value, a.StationLongitude!.Value))))
            .OrderBy(x => x.Miles)
            .Cast<(MediaAssignment, double)?>()
            .FirstOrDefault();
}
