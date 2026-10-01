namespace ContactConnection.Domain.Entities;

/// <summary>
/// One US ZIP Code's location (platform-wide, <c>public.zip_codes</c>; S171, Media Agency Phase B).
/// Imported from the zip-codes.com database — the primary record per ZIP only. Locates a caller from
/// the zip the sales script captures, for nearest-station media attribution.
/// </summary>
public class ZipCodeLocation
{
    public string Zip { get; set; } = "";
    public string? City { get; set; }
    public string? State { get; set; }
    public string? County { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>The telephone area codes serving the ZIP ("520/928" in the source).</summary>
    public string[] AreaCodes { get; set; } = [];
    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>
/// An area code's center — the mean location of the ZIPs it serves, so it leans toward where the ZIPs
/// (and people) cluster. Rebuilt with <see cref="ZipCodeLocation"/>. The fallback caller location when
/// no zip has been captured yet: the caller's phone number's area code.
/// </summary>
public class AreaCodeLocation
{
    public string AreaCode { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int ZipCount { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
