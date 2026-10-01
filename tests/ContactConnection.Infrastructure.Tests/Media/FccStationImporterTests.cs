using System.IO.Compression;
using System.Text;
using ContactConnection.Infrastructure.Media;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Media;

public class FccStationImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cc-fcc-test-" + Guid.NewGuid().ToString("N"));

    public FccStationImporterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // LMS .dat layout: pipe-delimited, header row of column names, every line ending "|^|".
    private void Table(string name, string header, params string[] rows)
    {
        using var zip = ZipFile.Open(Path.Combine(_dir, name + ".zip"), ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry(name + ".dat").Open(), Encoding.Latin1);
        w.WriteLine(header + "|^|");
        foreach (var r in rows) w.WriteLine(r + "|^|");
    }

    private void Seed(params string[] facilityRows)
    {
        Table("facility", "active_ind|callsign|community_served_city|community_served_state|facility_id|service_code|network_affiliation",
            facilityRows);
        Table("application_facility", "afac_application_id|afac_facility_id|create_ts",
            "app-old|100|2015-01-01 00:00:00",
            "app-new|100|2023-06-01 00:00:00",
            "app-noloc|100|2025-01-01 00:00:00",   // newest, but no location → skipped
            "app-200|200|2020-01-01 00:00:00");
        Table("app_location", "aloc_aapp_application_id|aloc_active_ind|aloc_lat_deg|aloc_lat_mm|aloc_lat_ss|aloc_lat_dir|aloc_long_deg|aloc_long_mm|aloc_long_ss|aloc_long_dir",
            "app-old|Y|10|0|0|N|20|0|0|W",
            "app-new|Y|34|13|37|N|118|4|1|W",
            "app-noloc|N|1|0|0|N|1|0|0|W",
            "app-200|Y|40|44|54|N|73|59|9|W");
    }

    [Fact]
    public void Build_UsesTheMostRecentApplicationThatHasALocation()
    {
        Seed("Y|KABC-TV|LOS ANGELES|CA|100|DTV|ABC");
        var s = Assert.Single(FccStationImporter.Build(_dir));
        Assert.Equal("KABC-TV", s.CallSign);
        Assert.Equal("Los Angeles", s.CommunityCity);
        Assert.Equal("ABC", s.NetworkAffiliation);
        Assert.Equal(34.226944, s.Latitude, 5);
        Assert.Equal(-118.066944, s.Longitude, 5);
    }

    [Theory]
    [InlineData("N|KABC-TV|LOS ANGELES|CA|100|DTV|ABC")]   // inactive
    [InlineData("Y|NEW|LOS ANGELES|CA|100|DTV|")]          // unbuilt permit, no call sign yet
    [InlineData("Y|K12AB-D|LOS ANGELES|CA|100|FX|")]       // translator
    [InlineData("Y|KXYZ-LP|LOS ANGELES|CA|100|LPA|")]      // analog low-power TV
    [InlineData("Y|XEWT|TIJUANA|BN|100|DTV|")]             // not a US community
    [InlineData("Y|WABC-TV|NEW YORK|NY|999|DTV|ABC")]      // no application with a location
    public void Build_SkipsStationsThatCantCarryAnAttributableAd(string row)
    {
        Seed(row);
        Assert.Empty(FccStationImporter.Build(_dir));
    }

    [Theory]
    [InlineData("34", "13", "37", "N", "S", 34.226944)]
    [InlineData("33", "52", "0.5", "S", "S", -33.866806)]
    [InlineData("118", "4", "1", "W", "W", -118.066944)]
    [InlineData("2", "0", "0", "E", "W", 2.0)]
    public void Dms_ConvertsToSignedDecimal(string d, string m, string s, string dir, string negative, double expected) =>
        Assert.Equal(expected, FccStationImporter.Dms(d, m, s, dir, negative)!.Value, 5);

    [Fact]
    public void Dms_MissingDegrees_IsNull() => Assert.Null(FccStationImporter.Dms("", "1", "1", "N", "S"));
}
