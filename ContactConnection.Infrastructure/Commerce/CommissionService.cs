using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>See <see cref="ICommissionService"/>. Uses its own tenant DbContext, so callers holding a
/// tracked CallRecord in another context aren't disturbed (it only writes commission columns).</summary>
public class CommissionService(ScopedTenantDbContextFactory dbFactory) : ICommissionService
{
    public Task RecalculateAsync(Guid callRecordId, string trigger, CancellationToken ct = default) =>
        WithRecordAsync(callRecordId, (db, record) => SyncAsync(db, record, trigger, ct), ct);

    public Task OrderSubmittedAsync(Guid callRecordId, CancellationToken ct = default) =>
        WithRecordAsync(callRecordId, (db, record) =>
        {
            record.MarkOrderSubmitted(DateTimeOffset.UtcNow);
            return SyncAsync(db, record, CommissionTrigger.OrderSubmitted, ct);
        }, ct);

    public Task ReverseAsync(Guid callRecordId, string reason, CancellationToken ct = default) =>
        WithRecordAsync(callRecordId, (db, record) =>
        {
            record.ReverseCommissions(reason, DateTimeOffset.UtcNow);
            return SyncAsync(db, record, string.IsNullOrWhiteSpace(reason) ? CommissionTrigger.OrderCancelled : reason.Trim(), ct);
        }, ct);

    public Task RestoreAsync(Guid callRecordId, CancellationToken ct = default) =>
        WithRecordAsync(callRecordId, (db, record) =>
        {
            record.RestoreCommissions();
            return SyncAsync(db, record, CommissionTrigger.Restored, ct);
        }, ct);

    private async Task WithRecordAsync(Guid callRecordId, Func<TenantDbContext, CallRecord, Task> work, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == callRecordId, ct);
        if (record is null) return;
        await work(db, record);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncAsync(TenantDbContext db, CallRecord record, string trigger, CancellationToken ct)
    {
        var desired = await DesiredAsync(db, record, ct);
        var inForce = await db.CommissionEntries
            .Where(e => e.CallRecordId == record.Id && e.EntryType == CommissionEntryType.Earned && !e.IsReversed)
            .ToListAsync(ct);

        static string Key(Guid agent, Guid? rule, decimal amount) => $"{agent}|{rule}|{amount:0.00}";
        var want = desired.Lines.Select(l => Key(desired.AgentId, l.RuleId, l.Amount)).Order().ToList();
        var have = inForce.Select(e => Key(e.AgentId, e.RuleId, e.Amount)).Order().ToList();
        if (want.SequenceEqual(have)) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in inForce) db.CommissionEntries.Add(entry.Reverse(trigger, now));
        foreach (var line in desired.Lines)
            db.CommissionEntries.Add(CommissionEntry.Earned(record.TenantId, record, desired.AgentId, line, now));
    }

    private static async Task<(Guid AgentId, List<CommissionLine> Lines)> DesiredAsync(TenantDbContext db, CallRecord record, CancellationToken ct)
    {
        if (record.AgentId is not { } agentId || record.CommissionsReversedAt is not null) return (Guid.Empty, []);

        // A campaign with any active rules uses only its own; otherwise its client's.
        var rules = await db.CommissionRules.AsNoTracking()
            .Where(r => r.IsActive && r.CampaignId == record.CampaignId).ToListAsync(ct);
        if (rules.Count == 0 && record.ClientId != Guid.Empty)
            rules = await db.CommissionRules.AsNoTracking()
                .Where(r => r.IsActive && r.ClientId == record.ClientId).ToListAsync(ct);
        if (rules.Count == 0) return (agentId, []);

        var facts = new CommissionCallFacts(
            record.OrderSubmittedAt is not null, record.Cart, record.RoutedTierLabel, CustomFieldValues(record.CustomFields));
        return (agentId, CommissionCalculator.Calculate(facts, rules));
    }

    /// <summary>The call_records.custom_fields snapshot as field name → text.</summary>
    internal static Dictionary<string, string> CustomFieldValues(string? snapshotJson)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(snapshotJson)) return values;
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return values;
            foreach (var p in doc.RootElement.EnumerateObject())
                values[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.Null or JsonValueKind.Undefined => "",
                    _ => p.Value.GetRawText(),
                };
        }
        catch (JsonException) { /* malformed snapshot — no flags */ }
        return values;
    }
}
