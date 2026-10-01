using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Media;

/// <summary>
/// Finds the media assignment in effect for a phone number on a date and copies it into a
/// <see cref="MediaAttribution"/> for the call record (S171, Media Agency Phase A/B). National first, else
/// the Local one nearest the caller, else the default Local one — see <see cref="MediaAssignmentRules.Resolve"/>.
/// </summary>
public static class MediaAttributionResolver
{
    /// <param name="caller">Where the caller is (see <see cref="CallerLocator"/>) — picks the nearest
    /// station among Local assignments. Null = the default Local one.</param>
    public static async Task<MediaAttribution?> ResolveAsync(
        TenantDbContext db, Guid phoneNumberId, string? dialedNumber, DateOnly date,
        CallerLocation? caller = null, CancellationToken ct = default)
    {
        var assignments = await db.MediaAssignments.AsNoTracking()
            .Where(a => a.PhoneNumberId == phoneNumberId && a.StartDate <= date)
            .ToListAsync(ct);
        var chosen = MediaAssignmentRules.Resolve(assignments, date, caller?.Point);
        if (chosen is null) return null;

        var agencyName = await db.MediaAgencies.AsNoTracking()
            .Where(g => g.Id == chosen.MediaAgencyId).Select(g => g.Name).FirstOrDefaultAsync(ct) ?? "";
        return MediaAttribution.From(chosen, agencyName, dialedNumber, caller);
    }

    /// <summary>"Today" in the tenant's time zone — agencies give start dates as local dates.</summary>
    public static DateOnly TodayIn(string? ianaTimeZone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(ianaTimeZone) ? "UTC" : ianaTimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        }
        catch (TimeZoneNotFoundException) { return DateOnly.FromDateTime(DateTime.UtcNow); }
    }
}
