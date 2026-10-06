using System.Globalization;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Kpis;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Reports;

/// <summary>One column the records widget can show (S181, docs/client-dashboards-plan.md §C). Custom fields are "cf:&lt;name&gt;".</summary>
public sealed record RecordColumn(string Key, string Label, string Group);

public static class RecordColumns
{
    public const string CustomFieldPrefix = "cf:";

    public static readonly IReadOnlyList<RecordColumn> All =
    [
        new("started", "Call time", "Call"),
        new("duration", "Duration", "Call"),
        new("campaign", "Campaign", "Call"),
        new("agent", "Agent", "Call"),
        new("callerId", "Caller number", "Call"),
        new("dnis", "Number dialed", "Call"),
        new("status", "Call status", "Call"),
        new("customerName", "Customer", "Contact"),
        new("phone", "Phone", "Contact"),
        new("email", "Email", "Contact"),
        new("billingAddress", "Billing address", "Address"),
        new("billingCityState", "Billing city / state", "Address"),
        new("billingZip", "Billing ZIP", "Address"),
        new("shippingAddress", "Shipping address", "Address"),
        new("shippingCityState", "Shipping city / state", "Address"),
        new("shippingZip", "Shipping ZIP", "Address"),
        new("agency", "Media agency", "Media"),
        new("station", "Station", "Media"),
        new("mediaType", "Media type", "Media"),
        new("disposition", "Disposition", "Outcome"),
        new("category", "Reporting category", "Outcome"),
        new("orderNumber", "Order number", "Order"),
        new("revenue", "Order total", "Order"),
        new("paymentStatus", "Payment", "Order"),
        new("recording", "Recording", "Recording"),
    ];

    public static readonly IReadOnlyList<string> Default = ["started", "campaign", "customerName", "phone", "disposition", "orderNumber", "revenue", "duration"];

    public static bool IsValid(string key) => All.Any(c => c.Key == key) || (key.StartsWith(CustomFieldPrefix) && key.Length > CustomFieldPrefix.Length);
}

/// <param name="CampaignIds">When set, only these campaigns (a client dashboard's locked scope); an empty set matches nothing.</param>
/// <param name="Filters">Column key → text the column's value must contain (case-insensitive).</param>
/// <param name="Sort">A column key; newest call first when unset.</param>
public sealed record RecordsQuery(DateTimeOffset Since, DateTimeOffset Until, Guid? ClientId, Guid? CampaignId, IReadOnlySet<Guid>? CampaignIds,
    IReadOnlyList<string> Columns, string? Search, IReadOnlyDictionary<string, string>? Filters, string? Sort, bool Descending,
    int Page, int PageSize, string TimeZone);

public sealed record RecordsRow(Guid Id, IReadOnlyDictionary<string, string?> Values);
public sealed record RecordsPage(int Total, int Page, int PageSize, bool Truncated, IReadOnlyList<RecordColumn> Columns, IReadOnlyList<RecordsRow> Rows);


/// <summary>
/// Call records for the records widget (S181). Production inbound + outbound calls created in the window, inside the
/// scope; free-text search and per-column "contains" filters; one page of rows with only the requested columns. Never
/// reads the PCI blob (sensitive_data) — it isn't a column and isn't projected.
/// </summary>
public sealed class CallRecordsReport(ScopedTenantDbContextFactory dbFactory)
{
    /// <summary>Rows considered per request — a wider window than this asks the viewer to narrow it.</summary>
    public const int MaxRows = 20_000;

    private sealed record Ix(Guid Id, Guid CallRecordId, int Number, Guid? CampaignId, Guid? AgentId, string? Disposition, Guid? DispositionId,
        string? OrderNumber, DateTimeOffset? OrderSubmittedAt, decimal? TotalAmount, decimal? CartTotal, string? PaymentStatus,
        DateTimeOffset? StartedAt, string? CustomFields);

    private sealed record Call(Guid Id, Guid CampaignId, Guid ClientId, Guid? AgentId, DateTimeOffset CreatedAt, DateTimeOffset? CallStartAt,
        int? HandleTimeSeconds, string? CallerId, string? Dnis, string OverallStatus, string? FirstName, string? LastName, string? Phone,
        string? BillingPhone, string? Email, Domain.ValueObjects.CallAddresses? Addresses, Domain.ValueObjects.MediaAttribution? Media,
        string? CustomFields, bool RecordingRetained, string? RecordingUrl);

    private sealed class Names
    {
        public Dictionary<Guid, string> Campaigns = [];
        public Dictionary<Guid, Guid> CampaignClient = [];
        public Dictionary<Guid, string> Agents = [];
        public Dictionary<Guid, string> DispositionCategory = [];
    }

    private static async Task<Names> NamesAsync(TenantDbContext db, CancellationToken ct)
    {
        var campaigns = await db.Campaigns.AsNoTracking().Select(c => new { c.Id, c.Name, c.ClientId }).ToListAsync(ct);
        var categories = await db.DispositionCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return new Names
        {
            Campaigns = campaigns.ToDictionary(c => c.Id, c => c.Name),
            CampaignClient = campaigns.ToDictionary(c => c.Id, c => c.ClientId),
            Agents = await db.Agents.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.FirstName + " " + a.LastName, ct),
            DispositionCategory = (await db.Dispositions.AsNoTracking().Select(d => new { d.Id, d.CategoryId }).ToListAsync(ct))
                .Where(d => categories.ContainsKey(d.CategoryId)).ToDictionary(d => d.Id, d => categories[d.CategoryId]),
        };
    }

    private static IQueryable<CallRecord> Production(TenantDbContext db) =>
        db.CallRecords.AsNoTracking().Where(r => r.RunMode == CallRunMode.Production);

    // Filters go on the entity above; this projection runs last (EF can't filter after a constructor projection).
    private static IQueryable<Call> Project(IQueryable<CallRecord> q) =>
        q.Select(r => new Call(r.Id, r.CampaignId, r.ClientId, r.AgentId, r.CreatedAt, r.CallStartAt, r.HandleTimeSeconds, r.CallerId, r.Dnis,
            r.OverallStatus, r.FirstName, r.LastName, r.Phone, r.BillingPhone, r.Email, r.Addresses, r.MediaAttribution, r.CustomFields,
            r.RecordingRetained, r.RecordingUrl));

    private static async Task<Dictionary<Guid, List<Ix>>> InteractionsAsync(TenantDbContext db, List<Guid> ids, CancellationToken ct)
    {
        var rows = new List<Ix>();
        foreach (var chunk in ids.Chunk(2000))
        {
            // The cart is a value-converted JSON column — load it whole and take its total here.
            var raw = await db.CallInteractions.AsNoTracking().Where(i => chunk.Contains(i.CallRecordId))
                .Select(i => new { i.Id, i.CallRecordId, i.InteractionNumber, i.CampaignId, i.AgentId, i.Disposition, i.DispositionId,
                    i.OrderNumber, i.OrderSubmittedAt, i.TotalAmount, i.Cart, i.PaymentStatus, i.StartedAt, i.CustomFields })
                .ToListAsync(ct);
            rows.AddRange(raw.Select(i => new Ix(i.Id, i.CallRecordId, i.InteractionNumber, i.CampaignId, i.AgentId, i.Disposition, i.DispositionId,
                i.OrderNumber, i.OrderSubmittedAt, i.TotalAmount, i.Cart?.CartTotal, i.PaymentStatus, i.StartedAt, i.CustomFields)));
        }
        return rows.GroupBy(i => i.CallRecordId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Number).ToList());
    }

    public async Task<RecordsPage> QueryAsync(RecordsQuery q, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var names = await NamesAsync(db, ct);
        var zone = ResolveZone(q.TimeZone);
        var columns = Resolve(q.Columns);

        var query = Production(db).Where(c => c.CreatedAt >= q.Since && c.CreatedAt < q.Until);
        if (q.CampaignId is { } cid) query = query.Where(c => c.CampaignId == cid);
        if (q.CampaignIds is { } set) { var list = set.ToList(); query = query.Where(c => list.Contains(c.CampaignId)); }
        if (q.ClientId is { } client)
        {
            var clientCampaigns = names.CampaignClient.Where(kv => kv.Value == client).Select(kv => kv.Key).ToList();
            query = query.Where(c => clientCampaigns.Contains(c.CampaignId));
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = $"%{q.Search.Trim().Replace("%", "").Replace("_", "")}%";
            var digits = new string(q.Search.Where(char.IsDigit).ToArray());
            var phoneTerm = digits.Length >= 3 ? $"%{digits}%" : term;
            // Campaign names match too ("LF" finds "NeuroQ - LF TV"), as do dispositions.
            var plain = q.Search.Trim();
            var campaignMatches = names.Campaigns.Where(kv => kv.Value.Contains(plain, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(c => campaignMatches.Contains(c.CampaignId)
                || db.CallInteractions.Any(i => i.CallRecordId == c.Id && EF.Functions.ILike(i.Disposition ?? "", term))
                || EF.Functions.ILike(c.FirstName ?? "", term) || EF.Functions.ILike(c.LastName ?? "", term)
                || EF.Functions.ILike(c.Email ?? "", term) || EF.Functions.ILike(c.CallerId ?? "", phoneTerm)
                || EF.Functions.ILike(c.Phone ?? "", phoneTerm) || EF.Functions.ILike(c.BillingPhone ?? "", phoneTerm)
                || db.CallInteractions.Any(i => i.CallRecordId == c.Id && EF.Functions.ILike(i.OrderNumber ?? "", term)));
        }

        var calls = await Project(query.OrderByDescending(c => c.CreatedAt).Take(MaxRows + 1)).ToListAsync(ct);
        var truncated = calls.Count > MaxRows;
        if (truncated) calls.RemoveAt(calls.Count - 1);
        var interactions = await InteractionsAsync(db, calls.Select(c => c.Id).ToList(), ct);

        // Values for the shown columns, plus any column being filtered or sorted on.
        var needed = columns.Select(c => c.Key)
            .Concat(q.Filters?.Keys.Where(RecordColumns.IsValid) ?? []).Concat(q.Sort is { } s && RecordColumns.IsValid(s) ? [s] : [])
            .Distinct().ToList();
        var rows = calls.Select(c => (Call: c, Values: needed.ToDictionary(k => k, k => Value(k, c, interactions.GetValueOrDefault(c.Id) ?? [], names, zone))))
            .ToList();

        if (q.Filters is { Count: > 0 })
            foreach (var (key, text) in q.Filters)
                if (!string.IsNullOrWhiteSpace(text) && needed.Contains(key))
                    rows = rows.Where(r => (r.Values[key] ?? "").Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (q.Sort is { } sort && needed.Contains(sort) && sort != "started")
        {
            var cmp = Comparer<(decimal? N, string? S)>.Create((a, b) => a.N is { } x && b.N is { } y ? x.CompareTo(y) : string.Compare(a.S, b.S, StringComparison.OrdinalIgnoreCase));
            rows = (q.Descending ? rows.OrderByDescending(r => SortKey(sort, r.Call, r.Values[sort]), cmp) : rows.OrderBy(r => SortKey(sort, r.Call, r.Values[sort]), cmp)).ToList();
        }
        else if (q.Sort == "started" && !q.Descending) rows.Reverse();

        var size = Math.Clamp(q.PageSize, 1, 200);
        var page = Math.Max(1, q.Page);
        var shown = columns.Select(c => c.Key).ToList();
        return new RecordsPage(rows.Count, page, size, truncated, columns,
            rows.Skip((page - 1) * size).Take(size)
                .Select(r => new RecordsRow(r.Call.Id, shown.ToDictionary(k => k, k => r.Values[k]))).ToList());
    }

    /// <summary>Whether this call is inside a dashboard's scope (detail view and recording playback).</summary>
    public async Task<bool> InScopeAsync(Guid id, Guid? clientId, IReadOnlySet<Guid>? campaignIds, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var call = await Project(Production(db).Where(c => c.Id == id)).FirstOrDefaultAsync(ct);
        return call is not null && InScope(call, clientId, campaignIds, await NamesAsync(db, ct));
    }

    private static bool InScope(Call call, Guid? clientId, IReadOnlySet<Guid>? campaignIds, Names names) =>
        (campaignIds is null || campaignIds.Contains(call.CampaignId))
        && (clientId is null || names.CampaignClient.GetValueOrDefault(call.CampaignId) == clientId);

    private static string RecordingState(Call c) =>
        !c.RecordingRetained ? "purged" : string.IsNullOrEmpty(c.RecordingUrl) ? "none" : "available";

    private static IReadOnlyList<RecordColumn> Resolve(IReadOnlyList<string> keys)
    {
        var list = (keys.Count == 0 ? RecordColumns.Default : keys).Where(RecordColumns.IsValid).Distinct()
            .Select(k => RecordColumns.All.FirstOrDefault(c => c.Key == k)
                         ?? new RecordColumn(k, k[RecordColumns.CustomFieldPrefix.Length..], "Custom fields")).ToList();
        return list.Count > 0 ? list : Resolve(RecordColumns.Default);
    }

    private static (decimal? N, string? S) SortKey(string key, Call c, string? value) => key switch
    {
        "duration" => (c.HandleTimeSeconds, value),
        "revenue" => (decimal.TryParse(value?.TrimStart('$').Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null, value),
        _ => (null, value),
    };

    private static string? Value(string key, Call c, List<Ix> ix, Names names, TimeZoneInfo zone)
    {
        var ordered = ix.Where(i => i.OrderSubmittedAt != null).ToList();
        string? Join(IEnumerable<string?> parts) => string.Join(" + ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct()) is { Length: > 0 } s ? s : null;
        var billing = c.Addresses?.Billing;
        var shipping = c.Addresses?.Shipping;
        string? Street(Domain.ValueObjects.AddressData? a) => a is null ? null
            : string.Join(" ", new[] { a.Street, a.UnitPrefix, a.Unit }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } st ? st : null;
        string? CityState(Domain.ValueObjects.AddressData? a) => a is null ? null
            : string.Join(", ", new[] { a.City, a.State }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } cs ? cs : null;

        return key switch
        {
            "started" => Format(c.CallStartAt ?? c.CreatedAt, zone),
            "duration" => c.HandleTimeSeconds is { } s ? (s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}") : null,
            "campaign" => c.CampaignId == Guid.Empty ? null : names.Campaigns.GetValueOrDefault(c.CampaignId),
            "agent" => Join(ix.Select(i => i.AgentId).Append(c.AgentId).OfType<Guid>().Select(a => names.Agents.GetValueOrDefault(a))),
            "callerId" => c.CallerId,
            "dnis" => c.Dnis,
            "status" => c.OverallStatus,
            "customerName" => string.Join(" ", new[] { c.FirstName, c.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } n ? n : null,
            "phone" => c.Phone ?? c.BillingPhone ?? c.CallerId,
            "email" => c.Email,
            "billingAddress" => Street(billing),
            "billingCityState" => CityState(billing),
            "billingZip" => billing?.Zip,
            "shippingAddress" => Street(shipping),
            "shippingCityState" => CityState(shipping),
            "shippingZip" => shipping?.Zip,
            "agency" => c.Media?.Agency,
            "station" => c.Media?.Station,
            "mediaType" => c.Media?.MediaType,
            "disposition" => Join(ix.Select(i => i.Disposition)),
            "category" => Join(ix.Select(i => i.DispositionId is { } d ? names.DispositionCategory.GetValueOrDefault(d) : null)),
            "orderNumber" => Join(ordered.Select(i => i.OrderNumber)),
            "revenue" => ordered.Count == 0 ? null : (ordered.Sum(i => i.TotalAmount ?? i.CartTotal ?? 0m)).ToString("C2", CultureInfo.GetCultureInfo("en-US")),
            "paymentStatus" => Join(ordered.Select(i => i.PaymentStatus)),
            "recording" => RecordingState(c) switch { "available" => "Yes", "purged" => "Deleted", _ => null },
            _ when key.StartsWith(RecordColumns.CustomFieldPrefix) => CustomField(key[RecordColumns.CustomFieldPrefix.Length..], c, ix),
            _ => null,
        };
    }

    private static string? CustomField(string name, Call c, List<Ix> ix) =>
        KpiService.FieldValue(c.CustomFields, name) ?? ix.Select(i => KpiService.FieldValue(i.CustomFields, name)).FirstOrDefault(v => v is not null);

    private static string Format(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd h:mm tt", CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
