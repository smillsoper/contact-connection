using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>
/// "Recalculate past calls" (S171): previews and applies commission recalculation for every call in a
/// scope (client / campaign / agent) that started in a time window, under the rules in effect when each
/// call started. Works in chunks so a large window never loads everything at once. Applying is
/// idempotent — re-running a batch (e.g. after a Worker restart) only writes what still differs.
/// </summary>
public static class CommissionRecalculator
{
    private const int ChunkSize = 250;

    public record Scope(Guid? ClientId, Guid? CampaignId, Guid? AgentId, DateTimeOffset From, DateTimeOffset To);

    public record AgentChange(Guid AgentId, decimal Current, decimal Recalculated, int ChangedCalls);

    public record Preview(int Calls, int ChangedCalls, decimal Current, decimal Recalculated, List<AgentChange> Agents);

    public static IQueryable<CallRecord> Calls(TenantDbContext db, Scope s)
    {
        var q = db.CallRecords.Where(r => r.CreatedAt >= s.From && r.CreatedAt < s.To);
        if (s.CampaignId is not null) q = q.Where(r => r.CampaignId == s.CampaignId);
        else if (s.ClientId is not null) q = q.Where(r => r.ClientId == s.ClientId);
        // An agent filter matches the call's agent now, or anyone holding commission on it.
        if (s.AgentId is { } agent)
            q = q.Where(r => r.AgentId == agent || db.CommissionEntries.Any(e => e.CallRecordId == r.Id && e.AgentId == agent));
        return q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id);
    }

    public static async Task<Preview> PreviewAsync(TenantDbContext db, Scope scope, CancellationToken ct)
    {
        var rules = await CommissionLedger.LoadRulesAsync(db, ct);
        var current = new Dictionary<Guid, decimal>();
        var recalculated = new Dictionary<Guid, decimal>();
        var changedByAgent = new Dictionary<Guid, int>();
        int calls = 0, changed = 0;

        for (var skip = 0; ; skip += ChunkSize)
        {
            var chunk = await Calls(db, scope).AsNoTracking().Skip(skip).Take(ChunkSize).ToListAsync(ct);
            if (chunk.Count == 0) break;
            var inForce = await InForceByCallAsync(db, chunk, ct);

            foreach (var record in chunk)
            {
                calls++;
                var desired = CommissionLedger.DesiredFor(record, rules);
                var have = inForce[record.Id].ToList();
                foreach (var e in have) current[e.AgentId] = current.GetValueOrDefault(e.AgentId) + e.Amount;
                if (desired.Lines.Count > 0) recalculated[desired.AgentId] = recalculated.GetValueOrDefault(desired.AgentId) + desired.Total;
                if (CommissionLedger.Matches(desired, have)) continue;

                changed++;
                foreach (var agent in have.Select(e => e.AgentId).Append(desired.AgentId).Where(a => a != Guid.Empty).Distinct())
                    changedByAgent[agent] = changedByAgent.GetValueOrDefault(agent) + 1;
            }
        }

        var agents = current.Keys.Union(recalculated.Keys)
            .Select(a => new AgentChange(a, current.GetValueOrDefault(a), recalculated.GetValueOrDefault(a), changedByAgent.GetValueOrDefault(a)))
            .Where(a => a.Current != 0 || a.Recalculated != 0 || a.ChangedCalls > 0)
            .ToList();
        return new Preview(calls, changed, current.Values.Sum(), recalculated.Values.Sum(), agents);
    }

    /// <summary>Applies a batch, saving progress after every chunk.</summary>
    public static async Task RunAsync(TenantDbContext db, CommissionRecalcBatch batch, CancellationToken ct)
    {
        var scope = new Scope(batch.ClientId, batch.CampaignId, batch.AgentId, batch.From, batch.To);
        var rules = await CommissionLedger.LoadRulesAsync(db, ct);
        batch.Start(await Calls(db, scope).CountAsync(ct));
        await db.SaveChangesAsync(ct);

        var note = $"Recalculated: {batch.Reason}";
        for (var skip = 0; ; skip += ChunkSize)
        {
            var chunk = await Calls(db, scope).Skip(skip).Take(ChunkSize).ToListAsync(ct);
            if (chunk.Count == 0) break;
            var inForce = await InForceByCallAsync(db, chunk, ct, tracked: true);

            int changed = 0;
            decimal difference = 0;
            foreach (var record in chunk)
            {
                var desired = CommissionLedger.DesiredFor(record, rules);
                var have = inForce[record.Id].ToList();
                if (CommissionLedger.Matches(desired, have)) continue;
                var at = batch.PostTo == CommissionPostTo.CallDate ? record.CreatedAt : DateTimeOffset.UtcNow;
                difference += CommissionLedger.Replace(db, record, desired, have, note, at, batch.Id);
                changed++;
            }
            batch.Progress(chunk.Count, changed, difference);
            await db.SaveChangesAsync(ct);

            // Keep the change tracker small on long runs — the batch row stays attached.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is not CommissionRecalcBatch).ToList())
                entry.State = EntityState.Detached;
        }
        batch.Complete();
        await db.SaveChangesAsync(ct);
    }

    private static async Task<ILookup<Guid, CommissionEntry>> InForceByCallAsync(
        TenantDbContext db, List<CallRecord> chunk, CancellationToken ct, bool tracked = false)
    {
        var ids = chunk.Select(r => r.Id).ToList();
        var q = db.CommissionEntries.Where(e => ids.Contains(e.CallRecordId) && e.EntryType == CommissionEntryType.Earned && !e.IsReversed);
        var list = tracked ? await q.ToListAsync(ct) : await q.AsNoTracking().ToListAsync(ct);
        return list.ToLookup(e => e.CallRecordId);
    }
}
