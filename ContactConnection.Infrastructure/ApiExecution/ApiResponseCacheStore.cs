using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Per-call store of successful API results (call_records.api_response_cache, a JSONB object keyed
/// by cache key) — what backs an api_call node's "once per call" option: an order submission that
/// already succeeded on this call replays its stored result instead of submitting again (a flow
/// looping back, an agent re-running a section, a script re-entering after a jump). Only successes
/// are stored, so a failed call is always retried for real.
/// </summary>
public interface IApiResponseCacheStore
{
    Task<string?> GetAsync(Guid callRecordId, string key, CancellationToken ct = default);
    Task SetAsync(Guid callRecordId, string key, string resultJson, CancellationToken ct = default);
}

public class ApiResponseCacheStore(ScopedTenantDbContextFactory factory) : IApiResponseCacheStore
{
    private TenantDbContext? _ctx;
    private TenantDbContext Ctx => _ctx ??= factory.Create();

    public async Task<string?> GetAsync(Guid callRecordId, string key, CancellationToken ct = default)
    {
        var rows = await Ctx.Database
            .SqlQuery<string?>($"""
                SELECT api_response_cache ->> {key} AS "Value"
                FROM call_records WHERE id = {callRecordId}
                """)
            .ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    public Task SetAsync(Guid callRecordId, string key, string resultJson, CancellationToken ct = default)
        // Single-statement merge — never read-modify-write, so two keys written concurrently can't
        // clobber each other. The value is stored as a JSON string (->> returns it verbatim).
        // ($$ raw string: {{x}} interpolates as a SQL parameter, so the literal '{}' needs no escaping.)
        => Ctx.Database.ExecuteSqlAsync($$"""
            UPDATE call_records
            SET api_response_cache = COALESCE(api_response_cache, '{}'::jsonb) || jsonb_build_object({{key}}::text, {{resultJson}}::text)
            WHERE id = {{callRecordId}}
            """, ct);
}
