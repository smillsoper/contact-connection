using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Dispositions;

/// <summary>See <see cref="IDispositionService"/>. Matching is by name or alias, trimmed and case-insensitive, in the
/// interaction's own scope (its campaign and that campaign's client); the narrowest scope wins, and an active disposition
/// beats a retired one with the same name (retired ones still match history).</summary>
public sealed class DispositionService(ScopedTenantDbContextFactory dbFactory, TenantContext tenantContext) : IDispositionService
{
    public const string FieldName = "disposition";

    public async Task<IReadOnlyList<DispositionCategory>> CategoriesAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        await EnsureSystemCategoriesAsync(db, ct);
        return await db.DispositionCategories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Disposition>> ForCampaignAsync(Guid? campaignId, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        Guid? clientId = campaignId is { } c
            ? await db.Campaigns.AsNoTracking().Where(x => x.Id == c).Select(x => (Guid?)x.ClientId).FirstOrDefaultAsync(ct)
            : null;
        var all = await db.Dispositions.AsNoTracking().Where(d => d.IsActive).ToListAsync(ct);
        return all.Where(d => d.AppliesTo(clientId, campaignId))
            .GroupBy(d => Disposition.Normalize(d.Name))
            .Select(g => g.OrderBy(d => d.ScopeRank).First())
            .OrderBy(d => d.DisplayOrder).ThenBy(d => d.Name)
            .ToList();
    }

    public async Task SyncCallAsync(Guid callRecordId, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var record = await db.CallRecords.Include(r => r.Interactions).FirstOrDefaultAsync(r => r.Id == callRecordId, ct);
        if (record is null) return;
        var catalog = await db.Dispositions.AsNoTracking().ToListAsync(ct);
        var clients = await CampaignClientsAsync(db, ct);

        foreach (var ix in record.Interactions.Where(i => i.Status != InteractionStatus.Active))
        {
            var campaignId = ix.CampaignId ?? record.CampaignId;
            var text = RecordedText(record, ix) ?? ix.Disposition;
            var match = Resolve(catalog, text, clients.GetValueOrDefault(campaignId, record.ClientId), campaignId);
            if (text != ix.Disposition || match?.Id != ix.DispositionId) ix.SetDisposition(text, match?.Id);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> RelinkAllAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var catalog = await db.Dispositions.AsNoTracking().ToListAsync(ct);
        var clients = await CampaignClientsAsync(db, ct);
        var rows = await db.CallInteractions.AsNoTracking()
            .Where(i => i.Disposition != null && i.Disposition != "")
            .Join(db.CallRecords, i => i.CallRecordId, r => r.Id,
                (i, r) => new { i.Id, i.Disposition, i.DispositionId, CampaignId = i.CampaignId ?? r.CampaignId, r.ClientId })
            .ToListAsync(ct);

        var changes = rows
            .Select(r => (r.Id, r.DispositionId, New: Resolve(catalog, r.Disposition, clients.GetValueOrDefault(r.CampaignId, r.ClientId), r.CampaignId)?.Id))
            .Where(x => x.New != x.DispositionId)
            .ToList();
        foreach (var group in changes.GroupBy(c => c.New))
        {
            var ids = group.Select(g => g.Id).ToList();
            var target = group.Key;
            foreach (var chunk in ids.Chunk(1000))
                await db.CallInteractions.Where(i => chunk.Contains(i.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.DispositionId, target), ct);
        }
        return changes.Count;
    }

    public async Task<IReadOnlyList<UnmappedDisposition>> UnmappedAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var rows = await db.CallInteractions.AsNoTracking()
            .Where(i => i.DispositionId == null && i.Disposition != null && i.Disposition != "")
            .Join(db.CallRecords, i => i.CallRecordId, r => r.Id,
                (i, r) => new { i.Disposition, CampaignId = i.CampaignId ?? r.CampaignId, r.RunMode, At = i.CompletedAt ?? i.StartedAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => Disposition.Normalize(r.Disposition))
            .Select(g => new UnmappedDisposition(
                g.GroupBy(x => x.Disposition!.Trim()).OrderByDescending(x => x.Count()).First().Key,
                g.Count(), g.Count(x => x.RunMode == CallRunMode.Production),
                g.Select(x => x.CampaignId).Where(c => c != Guid.Empty).Distinct().ToList(),
                g.Max(x => x.At)))
            .OrderByDescending(u => u.ProductionCount).ThenByDescending(u => u.Count)
            .ToList();
    }

    /// <summary>The catalog entry <paramref name="text"/> means for a call on this client / campaign, or null.</summary>
    public static Disposition? Resolve(IEnumerable<Disposition> catalog, string? text, Guid? clientId, Guid? campaignId) =>
        string.IsNullOrWhiteSpace(text) ? null
            : catalog.Where(d => d.Matches(text) && d.AppliesTo(clientId, campaignId))
                .OrderBy(d => d.IsActive ? 0 : 1).ThenBy(d => d.ScopeRank).FirstOrDefault();

    /// <summary>The disposition field as written now: a transferred interaction's own field (S178), else the record's —
    /// the same rule the flow engine uses at completion.</summary>
    public static string? RecordedText(CallRecord record, CallInteraction ix)
    {
        if (Field(ix.CustomFields) is { } own) return own;
        var transferred = ix.CampaignId is { } c && c != Guid.Empty && c != record.CampaignId;
        if (transferred) return null;
        // On a multi-interaction call the record's field belongs to the first interaction only.
        var first = record.Interactions.OrderBy(i => i.InteractionNumber).FirstOrDefault();
        return first is null || first.Id == ix.Id ? Field(record.CustomFields) : null;
    }

    private static string? Field(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json)?[FieldName] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
                ? s.Trim() : null;
        }
        catch (JsonException) { return null; }
    }

    private static async Task<Dictionary<Guid, Guid>> CampaignClientsAsync(TenantDbContext db, CancellationToken ct) =>
        await db.Campaigns.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ClientId, ct);

    private async Task EnsureSystemCategoriesAsync(TenantDbContext db, CancellationToken ct)
    {
        var existing = await db.DispositionCategories.Where(c => c.Key != null).Select(c => c.Key!).ToListAsync(ct);
        var tenantId = tenantContext.Current?.Id ?? Guid.Empty;
        var missing = DispositionCategory.SystemDefaults(tenantId).Where(c => !existing.Contains(c.Key!)).ToList();
        if (missing.Count == 0) return;
        db.DispositionCategories.AddRange(missing);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* another request seeded them first (unique key) */ }
    }
}
