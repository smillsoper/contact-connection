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
        string tenantSchemaName, List<Guid>? campaignIds, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var campaignFilterSql = campaignIds is not null ? "AND campaign_id = ANY(@campaignIds)" : "";
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
            GROUP BY campaign_id, state
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("completed", CallHistoryState.Completed);
        cmd.Parameters.AddWithValue("abandoned", CallHistoryState.Abandoned);
        if (campaignIds is not null)
            cmd.Parameters.Add(new NpgsqlParameter("campaignIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = campaignIds.ToArray() });

        var results = new List<CampaignStateCount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new CampaignStateCount(reader.GetGuid(0), reader.GetString(1), (int)reader.GetInt64(2)));

        return results;
    }
}
