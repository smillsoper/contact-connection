using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class CallRecordRepository : ICallRecordRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public CallRecordRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<CallRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.CallRecords.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddInteractionAsync(CallInteraction interaction, CancellationToken ct = default) =>
        await Db.CallInteractions.AddAsync(interaction, ct);

    public Task<CallRecord?> GetByIdWithInteractionsAsync(Guid id, CancellationToken ct = default) =>
        Db.CallRecords
            .Include(r => r.Interactions)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<CallRecord?> GetByContactIdExternalAsync(string contactIdExternal, CancellationToken ct = default) =>
        Db.CallRecords.FirstOrDefaultAsync(r => r.ContactIdExternal == contactIdExternal, ct);

    public async Task<CallRecordSearchPage> SearchAsync(CallRecordSearchCriteria c, CancellationToken ct = default)
    {
        // Interactions included: the list shows each interaction's order number and cart total (S178).
        var q = Db.CallRecords.AsNoTracking().Include(r => r.Interactions).AsQueryable();
        if (c.From is { } from) q = q.Where(r => r.CreatedAt >= from);
        if (c.To is { } to) q = q.Where(r => r.CreatedAt < to);
        if (c.CampaignId is { } campaignId) q = q.Where(r => r.CampaignId == campaignId);
        if (!string.IsNullOrWhiteSpace(c.OrderNumber))
        {
            var order = c.OrderNumber.Trim();
            q = q.Where(r => r.Interactions.Any(i => i.OrderNumber == order));
        }
        var digits = new string((c.Phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length > 0)
        {
            var like = $"%{digits}%";
            q = q.Where(r => EF.Functions.Like(r.CallerId ?? "", like)
                          || EF.Functions.Like(r.Phone ?? "", like)
                          || EF.Functions.Like(r.BillingPhone ?? "", like)
                          || EF.Functions.Like(r.ShippingPhone ?? "", like));
        }
        if (!string.IsNullOrWhiteSpace(c.Name))
        {
            var like = $"%{c.Name.Trim()}%";
            q = q.Where(r => EF.Functions.ILike(r.FirstName ?? "", like) || EF.Functions.ILike(r.LastName ?? "", like)
                          || EF.Functions.ILike((r.FirstName ?? "") + " " + (r.LastName ?? ""), like));
        }
        if (c.FailedApiCallsOnly)
        {
            var failed = Db.Database.SqlQueryRaw<Guid>(FailedApiCallsSql);
            q = q.Where(r => failed.Contains(r.Id));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt)
            .Skip(Math.Max(0, c.Skip)).Take(Math.Clamp(c.Take, 1, 200))
            .ToListAsync(ct);
        return new CallRecordSearchPage(items, total);
    }

    public async Task<IReadOnlySet<Guid>> FindWithFailedApiCallsAsync(
        IReadOnlyCollection<Guid> callRecordIds, CancellationToken ct = default)
    {
        if (callRecordIds.Count == 0) return new HashSet<Guid>();
        var ids = callRecordIds.ToArray();
        var failed = await Db.Database.SqlQueryRaw<Guid>(FailedApiCallsSql)
            .Where(id => ids.Contains(id))
            .ToListAsync(ct);
        return failed.ToHashSet();
    }

    // An api_call node writes {output}.success AND {output}.timed_out (ApiResponseWrapper) — the
    // .timed_out sibling tells it apart from a flattened response body that happens to contain a
    // "success" field (e.g. {output}.response.success). Unqualified table name: search_path routes
    // it to the tenant schema. The column is named "Value" so SqlQueryRaw<Guid> can compose on it.
    private const string FailedApiCallsSql = """
        SELECT DISTINCT s.call_record_id AS "Value"
        FROM flow_sessions s
        CROSS JOIN LATERAL jsonb_each_text(s.variable_store -> 'FlowVars') kv
        WHERE kv.key LIKE '%.success' AND kv.value = 'false'
          AND jsonb_exists(s.variable_store -> 'FlowVars', left(kv.key, length(kv.key) - 8) || '.timed_out')
        """;

    public async Task AddAsync(CallRecord record, CancellationToken ct = default) =>
        await Db.CallRecords.AddAsync(record, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<Guid>> FindRetainedRecordingIdsOldestFirstAsync(
        int limit, CancellationToken ct = default) =>
        await Db.CallRecords
            .Where(r => r.RecordingRetained && r.RecordingStartedAt != null)
            .OrderBy(r => r.CallEndAt ?? r.CreatedAt)
            .Select(r => r.Id)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> FindWithSensitiveDataOldestFirstAsync(
        int limit, CancellationToken ct = default) =>
        await Db.CallRecords
            .Where(r => r.SensitiveData != null)
            .OrderBy(r => r.SensitiveDataStoredAt ?? r.CreatedAt)
            .Select(r => r.Id)
            .Take(limit)
            .ToListAsync(ct);
}
