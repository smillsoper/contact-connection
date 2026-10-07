using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ContactConnection.Infrastructure.Repositories;

public class CallStateHistoryRepository(ITenantDbContextFactory factory) : ICallStateHistoryRepository
{
    public async Task AddAsync(CallStateHistoryEntry entry, string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        await db.CallStateHistory.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "Latest row per call, then group by campaign+state" is a top-1-per-group query EF Core's
    /// LINQ provider cannot translate (GroupBy().Select(g => g.OrderBy().First()) throws
    /// InvalidOperationException: 'EmptyProjectionMember' at translation time) — raw SQL via
    /// Postgres's DISTINCT ON is the standard way to express this, so this method bypasses EF's
    /// query pipeline and reads directly off the underlying Npgsql connection.
    /// </summary>
    public async Task<List<CampaignStateCount>> GetActiveStateCountsAsync(
        string tenantSchemaName, List<Guid>? campaignIds, CancellationToken ct = default, IReadOnlySet<string>? dnisKeys = null)
    {
        await using var db = factory.Create(tenantSchemaName);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var campaignFilterSql = campaignIds is not null ? "AND campaign_id = ANY(@campaignIds)" : "";
        // DNIS filter (S181): the call's dialed number, compared on its last 10 digits (see PhoneKey).
        var dnisFilterSql = dnisKeys is not null
            ? @"AND EXISTS (SELECT 1 FROM call_records r WHERE r.id = latest.call_record_id AND right(regexp_replace(coalesce(r.dnis, ''), '\D', '', 'g'), 10) = ANY(@dnisKeys))"
            : "";
        var sql = $"""
            WITH latest AS (
                SELECT DISTINCT ON (call_record_id) call_record_id, campaign_id, state
                FROM call_state_history
                ORDER BY call_record_id, sequence DESC
            )
            SELECT campaign_id, state, COUNT(*) AS cnt
            FROM latest
            WHERE state NOT IN (@completed, @abandoned)
            {campaignFilterSql}
            {dnisFilterSql}
            GROUP BY campaign_id, state
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("completed", CallHistoryState.Completed);
        cmd.Parameters.AddWithValue("abandoned", CallHistoryState.Abandoned);
        if (campaignIds is not null)
            cmd.Parameters.Add(new NpgsqlParameter("campaignIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = campaignIds.ToArray() });
        if (dnisKeys is not null)
            cmd.Parameters.Add(new NpgsqlParameter("dnisKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = dnisKeys.ToArray() });

        var results = new List<CampaignStateCount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new CampaignStateCount(reader.GetGuid(0), reader.GetString(1), (int)reader.GetInt64(2)));

        return results;
    }

    /// <summary>
    /// "Latest row per call, filtered to the non-terminal ones" — same top-1-per-group shape as
    /// <see cref="GetActiveStateCountsAsync"/> (EF's LINQ provider can't translate it), so this
    /// also drops to raw SQL via Postgres's DISTINCT ON.
    /// </summary>
    public async Task<List<NonTerminalCall>> GetNonTerminalCallsAsync(
        string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string sql = """
            WITH latest AS (
                SELECT DISTINCT ON (call_record_id) call_record_id, campaign_id, state
                FROM call_state_history
                ORDER BY call_record_id, sequence DESC
            )
            SELECT call_record_id, campaign_id
            FROM latest
            WHERE state NOT IN (@completed, @abandoned)
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("completed", CallHistoryState.Completed);
        cmd.Parameters.AddWithValue("abandoned", CallHistoryState.Abandoned);

        var results = new List<NonTerminalCall>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new NonTerminalCall(reader.GetGuid(0), reader.GetGuid(1)));

        return results;
    }

    public async Task<int> GetMaxSequenceAsync(
        string tenantSchemaName, Guid callRecordId, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        return await db.CallStateHistory
            .Where(e => e.CallRecordId == callRecordId)
            .Select(e => (int?)e.Sequence)
            .MaxAsync(ct) ?? 0;
    }

    public async Task<ServiceLevelStats> GetServiceLevelStatsAsync(
        string tenantSchemaName, List<Guid>? campaignIds, DateTimeOffset sinceUtc, CancellationToken ct = default,
        Guid? groupId = null, IReadOnlySet<string>? dnisKeys = null)
    {
        await using var db = factory.Create(tenantSchemaName);
        var query = db.CallStateHistory.Where(e => e.MetServiceLevel != null && e.EnteredAt >= sinceUtc);
        if (campaignIds is not null)
            query = query.Where(e => campaignIds.Contains(e.CampaignId));

        // Each call counts once per campaign — its first stamped answer. Rows stamped before S181 could carry a second
        // stamp for a re-bridge of the same queue entry (take-over / transfer), which isn't a second answer.
        var rows = await query.Select(e => new { e.CallRecordId, e.CampaignId, e.Sequence, e.MetServiceLevel, e.AgentId }).ToListAsync(ct);
        var firsts = rows.GroupBy(e => (e.CallRecordId, e.CampaignId)).Select(g => g.OrderBy(e => e.Sequence).First()).ToList();
        // Agent group (S181): calls the group handled — its interactions record the agent's groups at the time of the call.
        // DNIS: the number the caller dialed.
        if (groupId is { } g)
        {
            var callIds = firsts.Select(f => f.CallRecordId).Distinct().ToList();
            var inGroup = (await db.CallInteractions.AsNoTracking().Where(i => callIds.Contains(i.CallRecordId))
                    .Select(i => new { i.CallRecordId, i.RoutedGroupId, i.AgentGroupIds }).ToListAsync(ct))
                .Where(i => i.RoutedGroupId == g || i.AgentGroupIds.Contains(g)).Select(i => i.CallRecordId).ToHashSet();
            firsts = firsts.Where(f => inGroup.Contains(f.CallRecordId)).ToList();
        }
        if (dnisKeys is not null)
        {
            var ids = firsts.Select(f => f.CallRecordId).Distinct().ToList();
            var dnis = await db.CallRecords.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Dnis }).ToDictionaryAsync(r => r.Id, r => r.Dnis, ct);
            firsts = firsts.Where(f => Domain.ValueObjects.PhoneKey.Matches(dnisKeys, dnis.GetValueOrDefault(f.CallRecordId))).ToList();
        }
        return new ServiceLevelStats(firsts.Count(f => f.MetServiceLevel == true), firsts.Count(f => f.MetServiceLevel == false));
    }
}
