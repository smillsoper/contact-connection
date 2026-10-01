using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Media;

/// <summary>See <see cref="IMediaReattributionService"/>.</summary>
public class MediaReattributionService(
    ContactConnectionDbContext platformDb, ScopedTenantDbContextFactory dbFactory, TenantContext tenant,
    ICallRecordRepository callRecords) : IMediaReattributionService
{
    public async Task<MediaAttribution?> ApplyZipAsync(Guid callRecordId, string? zip, CancellationToken ct = default)
    {
        var record = await callRecords.GetByIdAsync(callRecordId, ct);
        if (record is null || await ForZipAsync(record, zip, ct) is not { } attribution) return null;
        record.SetMediaAttribution(attribution);
        await callRecords.SaveChangesAsync(ct);
        return attribution;
    }

    public async Task<MediaAttribution?> ForZipAsync(CallRecord record, string? zip, CancellationToken ct = default)
    {
        var current = record.MediaAttribution;
        if (current is null || current.MarketType != MediaMarketType.Local) return null;   // National ignores location
        if (CallerLocator.Zip5(zip) is not { } zip5) return null;
        if (current.LocationSource == CallerLocation.FromZip && current.LocationKey == zip5) return null;

        // The newest zip is the best word on where the caller is, so it always replaces an earlier
        // placement — even one from a different zip. A zip missing from the table falls back to the
        // caller's area code, then to the default Local assignment; never to a stale earlier zip (S171).
        var caller = await CallerLocator.LocateAsync(platformDb, zip5, record.CallerId, ct);

        await using var db = dbFactory.Create();
        // The number the call arrived on (Dnis); the attributed assignment's number if that's gone.
        var phoneNumberId = await db.PhoneNumbers.AsNoTracking().Where(p => p.Number == record.Dnis)
                                .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct)
                         ?? await db.MediaAssignments.AsNoTracking().Where(a => a.Id == current.AssignmentId)
                                .Select(a => (Guid?)a.PhoneNumberId).FirstOrDefaultAsync(ct);
        if (phoneNumberId is null) return null;

        // The assignments in effect on the day the call came in (tenant's local date), not today.
        var callDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(record.CreatedAt, TimeZone(tenant.Current?.Timezone)).DateTime);
        return await MediaAttributionResolver.ResolveAsync(db, phoneNumberId.Value, current.PhoneNumber, callDate, caller, ct);
    }

    private static TimeZoneInfo TimeZone(string? iana)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(iana) ? "UTC" : iana); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }
}
