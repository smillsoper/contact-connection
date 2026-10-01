using System.Text;
using ContactConnection.Infrastructure.Media;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Media;

/// <summary>S171 Phase B — the zip-codes.com Standard CSV (rows copied from the published sample).</summary>
public class ZipCodeImporterTests
{
    private const string Header =
        "ZipCode,City,State,County,AreaCode,CityType,CityAliasAbbreviation,CityAliasName,Latitude,Longitude,TimeZone,Elevation,CountyFIPS,DayLightSaving,PreferredLastLineKey,ClassificationCode,MultiCounty,StateFIPS,CityStateKey,CityAliasCode,PrimaryRecord,CityMixedCase,CityAliasMixedCase,StateANSI,CountyANSI,FacilityCode,CityDeliveryIndicator,CarrierRouteRateSortation,FinanceNumber,UniqueZIPName,CountyMixedCase";

    private static Stream Csv(params string[] rows) =>
        new MemoryStream(Encoding.Latin1.GetBytes(string.Join("\r\n", [Header, .. rows])));

    private static string Row(string zip, string areaCode, string lat, string lon, string primary, string city = "ADAMSVILLE") =>
        $"\"{zip}\",\"{city}\",\"RI\",\"NEWPORT\",\"{areaCode}\",\"Z\",\"\",\"{city}\",\"{lat}\",\"{lon}\",\"5\",\"34\",\"005\",\"Y\",\"V25833\",\"P\",\" \",\"44\",\"V25833\",\"\",\"{primary}\",\"Adamsville\",\"Adamsville\",\"44\",\"005\",\"P\",\"N\",\"C\",\"430280\",\"\",\"Newport\"";

    [Fact]
    public void Parse_KeepsPrimaryRecords_WithMixedCaseNames_AndSplitAreaCodes()
    {
        var zips = ZipCodeImporter.Parse(Csv(
            Row("02801", "401", "41.5544", "-71.1314", "P"),
            Row("02801", "401", "41.5544", "-71.1314", " ", city: "ALIAS"),   // alias row — skipped
            Row("86001", "520/928", "35.2", "-111.6", "P")));

        Assert.Equal(2, zips.Count);
        var ri = zips.Single(z => z.Zip == "02801");
        Assert.Equal("Adamsville", ri.City);
        Assert.Equal("Newport", ri.County);
        Assert.Equal(41.5544, ri.Latitude);
        Assert.Equal(["520", "928"], zips.Single(z => z.Zip == "86001").AreaCodes);
    }

    [Fact]
    public void AreaCodeCenters_AverageTheZipsEachAreaCodeServes()
    {
        var zips = ZipCodeImporter.Parse(Csv(
            Row("86001", "928", "35", "-111", "P"),
            Row("86002", "520/928", "33", "-113", "P")));
        var centers = ZipCodeImporter.AreaCodeCenters(zips).ToDictionary(a => a.AreaCode);

        Assert.Equal(34, centers["928"].Latitude);
        Assert.Equal(-112, centers["928"].Longitude);
        Assert.Equal(2, centers["928"].ZipCount);
        Assert.Equal(1, centers["520"].ZipCount);
    }

    [Fact]
    public void Parse_NotTheZipCodesCsv_SaysSo() =>
        Assert.Throws<InvalidDataException>(() => ZipCodeImporter.Parse(new MemoryStream(Encoding.Latin1.GetBytes("a,b,c\r\n1,2,3"))));

    [Fact]
    public void CsvFields_HandlesQuotedCommasAndEscapedQuotes() =>
        Assert.Equal(["a,b", "say \"hi\"", "", "x"], ZipCodeImporter.CsvFields("\"a,b\",\"say \"\"hi\"\"\",,x"));

    [Theory]
    [InlineData("+19285551234", "928")]
    [InlineData("(928) 555-1234", "928")]
    [InlineData("9285551234", "928")]
    [InlineData("5551234", null)]
    [InlineData("+441234567890", null)]
    [InlineData(null, null)]
    public void AreaCode_FromNanpNumbers(string? phone, string? expected) => Assert.Equal(expected, CallerLocator.AreaCode(phone));

    [Theory]
    [InlineData("85001", "85001")]
    [InlineData("85001-1234", "85001")]
    [InlineData(" 02801 ", "02801")]
    [InlineData("8500", null)]
    [InlineData("K1A 0B1", null)]
    public void Zip5_TakesTheFiveDigitZip(string zip, string? expected) => Assert.Equal(expected, CallerLocator.Zip5(zip));
}
