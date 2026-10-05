using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Billing;

/// <summary>See <see cref="IUsageMeter"/> — the one place billable minutes are counted (Portal usage card, invoices).</summary>
public class UsageMeter(ITenantDbContextFactory dbFactory) : IUsageMeter
{
    public static TimeZoneInfo ZoneFor(Tenant tenant)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tenant.Timezone) ? "America/Los_Angeles" : tenant.Timezone); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    public async Task<UsageMonth> TallyMonthAsync(Tenant tenant, int year, int month, CancellationToken ct = default)
    {
        var zone = ZoneFor(tenant);
        var first = new DateTime(year, month, 1);
        var next = first.AddMonths(1);
        var from = new DateTimeOffset(first, zone.GetUtcOffset(first)).ToUniversalTime();
        var to = new DateTimeOffset(next, zone.GetUtcOffset(next)).ToUniversalTime();

        await using var db = dbFactory.Create(tenant.SchemaName);
        var calls = await db.CallRecords.AsNoTracking()
            .Where(r => r.CallStartAt >= from && r.CallStartAt < to && r.RunMode == CallRunMode.Production)
            .Select(r => new { r.Source, r.CallerId, r.Dnis, r.CallStartAt, r.DisconnectedAt, Closed = r.CallEndAt != null })
            .ToListAsync(ct);

        var tally = new UsageTally();
        foreach (var c in calls)
            tally.Add(new MeteredCall(c.Source, c.CallerId, c.Dnis, c.CallStartAt, c.DisconnectedAt, c.Closed));

        return new UsageMonth(tally, from, to, zone.Id, DateOnly.FromDateTime(first), DateOnly.FromDateTime(next.AddDays(-1)));
    }
}
