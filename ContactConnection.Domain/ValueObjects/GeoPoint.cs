namespace ContactConnection.Domain.ValueObjects;

/// <summary>A latitude/longitude in decimal degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusMiles = 3958.8;

    /// <summary>Great-circle (haversine) distance in miles.</summary>
    public double MilesTo(GeoPoint other)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(other.Latitude - Latitude);
        var dLon = Rad(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
              + Math.Cos(Rad(Latitude)) * Math.Cos(Rad(other.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMiles * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

/// <summary>Where a caller is believed to be, and how we know.</summary>
public record CallerLocation(GeoPoint Point, string Source, string Key)
{
    public const string FromZip = "zip";
    public const string FromAreaCode = "area_code";
}
