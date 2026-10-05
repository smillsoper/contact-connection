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
        WithRecordAsync(callRecordId, (db, record) => CommissionLedger.SyncAsync(db, record, trigger, ct), ct);

    public Task OrderSubmittedAsync(Guid callRecordId, CancellationToken ct = default, Guid? interactionId = null) =>
        WithRecordAsync(callRecordId, (db, record) =>
        {
            var now = DateTimeOffset.UtcNow;
            var ix = record.CommerceInteraction(interactionId);
            ix?.MarkOrderSubmitted(now);
            return CommissionLedger.SyncAsync(db, record, CommissionTrigger.OrderSubmitted, ct);
        }, ct);

    public Task ReverseAsync(Guid callRecordId, string reason, CancellationToken ct = default) =>
        WithRecordAsync(callRecordId, (db, record) =>
        {
            record.ReverseCommissions(reason, DateTimeOffset.UtcNow);
            return CommissionLedger.SyncAsync(db, record, string.IsNullOrWhiteSpace(reason) ? CommissionTrigger.OrderCancelled : reason.Trim(), ct);
        }, ct);

    public Task RestoreAsync(Guid callRecordId, CancellationToken ct = default) =>
        WithRecordAsync(callRecordId, (db, record) =>
        {
            record.RestoreCommissions();
            return CommissionLedger.SyncAsync(db, record, CommissionTrigger.Restored, ct);
        }, ct);

    private async Task WithRecordAsync(Guid callRecordId, Func<TenantDbContext, CallRecord, Task> work, CancellationToken ct)
    {
        await using var db = dbFactory.Create();
        var record = await db.CallRecords.Include(r => r.Interactions).FirstOrDefaultAsync(r => r.Id == callRecordId, ct);
        if (record is null) return;
        await work(db, record);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// The commission ledger mechanics shared by single-call recalculation and "recalculate past calls"
/// (S171): what a call should earn under the rules in effect when it started, whether that differs from
/// the entries in force, and writing the reversals + new entries when it does.
/// </summary>
public static class CommissionLedger
{
    /// <summary>A tenant's commission rules indexed by campaign and client.</summary>
    public sealed class RuleBook(List<CommissionRule> rules)
    {
        private readonly ILookup<Guid, CommissionRule> _byCampaign = rules.Where(r => r.CampaignId is not null).ToLookup(r => r.CampaignId!.Value);
        private readonly ILookup<Guid, CommissionRule> _byClient = rules.Where(r => r.ClientId is not null).ToLookup(r => r.ClientId!.Value);

        public List<CommissionRule> For(CallRecord r) => For(r.CampaignId, r.ClientId, r.CreatedAt);

        public List<CommissionRule> For(Guid campaignId, Guid clientId, DateTimeOffset at) =>
            CommissionCalculator.RulesFor(_byCampaign[campaignId], clientId == Guid.Empty ? [] : _byClient[clientId], at);
    }

    public static async Task<RuleBook> LoadRulesAsync(TenantDbContext db, CancellationToken ct) =>
        new(await db.CommissionRules.AsNoTracking().ToListAsync(ct));

    /// <summary>One commission line for one agent, under the campaign it was earned for.</summary>
    public record EarnedLine(Guid AgentId, Guid CampaignId, CommissionLine Line);

    public record Desired(List<EarnedLine> Lines)
    {
        public decimal Total => Lines.Sum(l => l.Line.Amount);
        public IEnumerable<Guid> Agents => Lines.Select(l => l.AgentId).Distinct();
    }

    /// <summary>
    /// What the call should earn now — nothing while reversed by an admin. Interaction-scoped (S178):
    /// <list type="bullet">
    /// <item>The call's own work — its agent, under the call's campaign rules, with the first interaction's cart / order
    /// / routing tier and the record's custom fields. Exactly the pre-S178 calculation, so calls that weren't transferred
    /// are unchanged, and an agent relaunching a script on the same campaign isn't paid twice.</item>
    /// <item>Plus each transferred interaction (a different campaign, e.g. a CS agent who placed their own order) — its
    /// agent, under its campaign's rules, with its own cart / order / tier / fields.</item>
    /// </list>
    /// </summary>
    public static Desired DesiredFor(CallRecord record, RuleBook rules)
    {
        if (record.CommissionsReversedAt is not null) return new([]);
        var lines = new List<EarnedLine>();

        if (record.AgentId is { } agentId)
        {
            var first = record.FirstInteraction;
            var facts = new CommissionCallFacts(
                first?.OrderSubmittedAt is not null,
                first?.Cart,
                first?.RoutedTierLabel,
                CustomFieldValues(record.CustomFields), record.CreatedAt);
            lines.AddRange(CommissionCalculator.Calculate(facts, rules.For(record))
                .Select(l => new EarnedLine(agentId, record.CampaignId, l)));
        }

        foreach (var ix in record.Interactions)
        {
            if (ix.AgentId is not { } ixAgent || ix.CampaignId is not { } ixCampaign || ixCampaign == record.CampaignId)
                continue;
            var facts = new CommissionCallFacts(
                ix.OrderSubmittedAt is not null, ix.Cart, ix.RoutedTierLabel, CustomFieldValues(ix.CustomFields), record.CreatedAt);
            lines.AddRange(CommissionCalculator.Calculate(facts, rules.For(ixCampaign, record.ClientId, record.CreatedAt))
                .Select(l => new EarnedLine(ixAgent, ixCampaign, l)));
        }
        return new(lines);
    }

    public static bool Matches(Desired desired, IEnumerable<CommissionEntry> inForce)
    {
        static string Key(Guid agent, Guid? rule, decimal amount) => $"{agent}|{rule}|{amount:0.00}";
        var want = desired.Lines.Select(l => Key(l.AgentId, l.Line.RuleId, l.Line.Amount)).Order();
        var have = inForce.Select(e => Key(e.AgentId, e.RuleId, e.Amount)).Order();
        return want.SequenceEqual(have);
    }

    /// <summary>Reverses the entries in force and writes the desired ones, posted at <paramref name="at"/>.
    /// Returns the change in the call's net commission.</summary>
    public static decimal Replace(
        TenantDbContext db, CallRecord record, Desired desired, List<CommissionEntry> inForce,
        string note, DateTimeOffset at, Guid? batchId = null)
    {
        foreach (var entry in inForce) db.CommissionEntries.Add(entry.Reverse(note, at, batchId));
        foreach (var l in desired.Lines)
            db.CommissionEntries.Add(CommissionEntry.Earned(record.TenantId, record, l.AgentId, l.Line, at, batchId, l.CampaignId));
        return desired.Total - inForce.Sum(e => e.Amount);
    }

    public static Task<List<CommissionEntry>> InForceAsync(TenantDbContext db, Guid callRecordId, CancellationToken ct) =>
        db.CommissionEntries
            .Where(e => e.CallRecordId == callRecordId && e.EntryType == CommissionEntryType.Earned && !e.IsReversed)
            .ToListAsync(ct);

    /// <summary>Single-call recalculation, posted now.</summary>
    public static async Task SyncAsync(TenantDbContext db, CallRecord record, string trigger, CancellationToken ct)
    {
        var desired = DesiredFor(record, await LoadRulesAsync(db, ct));
        var inForce = await InForceAsync(db, record.Id, ct);
        if (!Matches(desired, inForce)) Replace(db, record, desired, inForce, trigger, DateTimeOffset.UtcNow);
    }

    /// <summary>The call_records.custom_fields snapshot as field name → text.</summary>
    public static Dictionary<string, string> CustomFieldValues(string? snapshotJson)
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
