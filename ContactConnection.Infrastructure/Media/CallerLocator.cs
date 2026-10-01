using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Media;

/// <summary>
/// Where a caller probably is, for nearest-station media attribution (S171, Phase B): the zip the
/// script captured is best; otherwise the center of their phone number's area code. SIP carries no
/// caller location on ordinary (non-911) calls, so these are the sources there are.
/// </summary>
public static class CallerLocator
{
    public static async Task<CallerLocation?> LocateAsync(
        ContactConnectionDbContext platformDb, string? zip, string? phone, CancellationToken ct = default)
    {
        if (Zip5(zip) is { } z)
        {
            var hit = await platformDb.ZipCodes.AsNoTracking().Where(x => x.Zip == z)
                .Select(x => new { x.Latitude, x.Longitude }).FirstOrDefaultAsync(ct);
            if (hit is not null) return new CallerLocation(new GeoPoint(hit.Latitude, hit.Longitude), CallerLocation.FromZip, z);
        }
        if (AreaCode(phone) is { } npa)
        {
            var hit = await platformDb.AreaCodes.AsNoTracking().Where(x => x.AreaCode == npa)
                .Select(x => new { x.Latitude, x.Longitude }).FirstOrDefaultAsync(ct);
            if (hit is not null) return new CallerLocation(new GeoPoint(hit.Latitude, hit.Longitude), CallerLocation.FromAreaCode, npa);
        }
        return null;
    }

    /// <summary>"85001", "85001-1234" → "85001"; anything else → null.</summary>
    public static string? Zip5(string? zip)
    {
        var digits = new string((zip ?? "").TakeWhile(c => c != '-').Where(char.IsDigit).ToArray());
        return digits.Length == 5 ? digits : null;
    }

    /// <summary>A NANP number's area code: "+19285551234", "(928) 555-1234", "9285551234" → "928".</summary>
    public static string? AreaCode(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        return digits.Length == 10 && digits[0] is >= '2' and <= '9' ? digits[..3] : null;
    }
}
