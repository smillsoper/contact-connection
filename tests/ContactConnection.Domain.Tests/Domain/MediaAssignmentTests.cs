using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S171 Media Agency Phase A — National hand-over, Local default, and call-date resolution.</summary>
public class MediaAssignmentTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Number = Guid.NewGuid(), Agency = Guid.NewGuid();
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static MediaAssignment National(string station, DateOnly start) =>
        MediaAssignment.Create(Tenant, Number, MediaMarketType.National, Agency, station, start);

    private static MediaAssignment Local(string station, DateOnly start) =>
        MediaAssignment.Create(Tenant, Number, MediaMarketType.Local, Agency, station, start);

    [Fact]
    public void NewNational_EndsThePreviousOne_TheDayBefore()
    {
        var old = National("CNN", D(1, 1));
        var next = National("FOX", D(3, 1));
        var changed = MediaAssignmentRules.FitNational(next, [old]);

        Assert.Equal(D(2, 28), old.EndDate);
        Assert.Contains(old, changed);
        Assert.Null(next.EndDate);
    }

    [Fact]
    public void NewNational_InsertedBeforeAScheduledOne_EndsBeforeIt()
    {
        var old = National("CNN", D(1, 1));
        var scheduled = National("FOX", D(6, 1));
        MediaAssignmentRules.FitNational(scheduled, [old]);

        var inserted = National("HLN", D(4, 1));
        MediaAssignmentRules.FitNational(inserted, [old, scheduled]);

        Assert.Equal(D(3, 31), old.EndDate);
        Assert.Equal(D(5, 31), inserted.EndDate);
        Assert.Null(scheduled.EndDate);
    }

    [Fact]
    public void TwoNationals_CantStartTheSameDay()
    {
        var old = National("CNN", D(1, 1));
        Assert.Throws<InvalidOperationException>(() => MediaAssignmentRules.FitNational(National("FOX", D(1, 1)), [old]));
    }

    [Fact]
    public void Resolve_PicksTheNationalInEffect_OnTheCallDate()
    {
        var a = National("CNN", D(1, 1));
        var b = National("FOX", D(3, 1));
        MediaAssignmentRules.FitNational(b, [a]);

        Assert.Equal("CNN", MediaAssignmentRules.Resolve([a, b], D(2, 15))!.Station);
        Assert.Equal("FOX", MediaAssignmentRules.Resolve([a, b], D(3, 1))!.Station);
        Assert.Null(MediaAssignmentRules.Resolve([a, b], D(12, 31).AddYears(-1)));
    }

    [Fact]
    public void Resolve_FallsBackToTheDefaultLocal()
    {
        var dallas = Local("KDFW", D(1, 1));
        var houston = Local("KRIV", D(2, 1));
        MediaAssignmentRules.MakeDefaultLocal(dallas, [dallas, houston]);

        Assert.Equal("KDFW", MediaAssignmentRules.Resolve([dallas, houston], D(3, 1))!.Station);
    }

    [Fact]
    public void OnlyOneDefaultLocal_AtATime()
    {
        var a = Local("KDFW", D(1, 1));
        var b = Local("KRIV", D(1, 1));
        MediaAssignmentRules.MakeDefaultLocal(a, [a, b]);
        MediaAssignmentRules.MakeDefaultLocal(b, [a, b]);

        Assert.False(a.IsDefaultLocal);
        Assert.True(b.IsDefaultLocal);
    }

    [Fact]
    public void National_IsNeverADefaultLocal_AndCantEndBeforeItStarts()
    {
        var n = National("CNN", D(5, 1));
        n.SetDefaultLocal(true);
        Assert.False(n.IsDefaultLocal);
        Assert.Throws<ArgumentException>(() => n.SetEndDate(D(4, 30)));
    }

    // ── Phase B: nearest station ─────────────────────────────────────────────

    private static readonly GeoPoint Phoenix = new(33.4484, -112.0740), Flagstaff = new(35.1983, -111.6513);

    private static MediaAssignment LocatedLocal(string station, GeoPoint at, bool isDefault = false)
    {
        var a = Local(station, D(1, 1));
        a.SetStation(station, Math.Abs(station.GetHashCode()), at.Latitude, at.Longitude);
        a.SetDefaultLocal(isDefault);
        return a;
    }

    [Fact]
    public void Local_WithCallerLocation_GoesToTheNearestStation()
    {
        var phx = LocatedLocal("KNXV-TV", Phoenix, isDefault: true);
        var flg = LocatedLocal("KNAZ-TV", Flagstaff);
        var callerInSedona = new GeoPoint(34.8697, -111.7610);

        Assert.Same(flg, MediaAssignmentRules.Resolve([phx, flg], D(5, 1), callerInSedona));
        Assert.Same(phx, MediaAssignmentRules.Resolve([phx, flg], D(5, 1)));   // no location → default
    }

    [Fact]
    public void Local_NoStationsLocated_FallsBackToTheDefault()
    {
        var a = Local("Newspaper", D(1, 1));
        var b = Local("Billboard", D(1, 1));
        b.SetDefaultLocal(true);
        Assert.Same(b, MediaAssignmentRules.Resolve([a, b], D(5, 1), Phoenix));
    }

    [Fact]
    public void National_WinsRegardlessOfLocation()
    {
        var n = National("CNN", D(1, 1));
        var flg = LocatedLocal("KNAZ-TV", Flagstaff);
        Assert.Same(n, MediaAssignmentRules.Resolve([n, flg], D(5, 1), Flagstaff));
    }

    [Fact]
    public void FreeTextStation_ClearsTheFccLink()
    {
        var a = LocatedLocal("KNAZ-TV", Flagstaff);
        a.SetStation("KNAZ");
        Assert.Null(a.StationFacilityId);
        Assert.Null(a.StationLatitude);
    }

    [Fact]
    public void Attribution_RecordsHowTheCallWasPlaced()
    {
        var flg = LocatedLocal("KNAZ-TV", Flagstaff);
        var caller = new CallerLocation(Phoenix, CallerLocation.FromZip, "85001");
        var m = MediaAttribution.From(flg, "Cannella", "+18005551234", caller);

        Assert.Equal("zip", m.LocationSource);
        Assert.Equal("85001", m.LocationKey);
        Assert.InRange(m.DistanceMiles!.Value, 115, 125);   // Phoenix → Flagstaff ≈ 120 mi
    }

    [Fact]
    public void GeoPoint_MilesTo_IsGreatCircleDistance() =>
        Assert.InRange(new GeoPoint(40.7128, -74.0060).MilesTo(new GeoPoint(34.0522, -118.2437)), 2440, 2460);   // NYC → LA
}
