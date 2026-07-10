using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

/// <summary>
/// Uses ITenantDbContextFactory directly (explicit schema per call) rather than
/// ScopedTenantDbContextFactory/TenantContext — this repository is written to from the
/// ESL background service, which has no HTTP request and therefore no populated TenantContext.
/// </summary>
public class CallTraceEventRepository(ITenantDbContextFactory factory) : ICallTraceEventRepository
{
    public async Task AddAsync(CallTraceEvent trace, string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        await db.CallTraceEvents.AddAsync(trace, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<CallTraceEvent>> GetByCallRecordIdAsync(
        Guid callRecordId, string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        return await db.CallTraceEvents
            .Where(e => e.CallRecordId == callRecordId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
    }

    public async Task<List<CallTraceCallSummary>> SearchCallsAsync(
        Guid tenantId,
        string tenantSchemaName,
        Guid? campaignId,
        Guid? flowId,
        string? dnis,
        string? ani,
        int limit,
        CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);

        // Sequence 1 is always the telephony engine's first step, which carries the
        // denormalized filter fields — querying it avoids scanning every step of every call.
        var query = db.CallTraceEvents.Where(e => e.TenantId == tenantId && e.Sequence == 1);

        if (campaignId.HasValue) query = query.Where(e => e.CampaignId == campaignId);
        if (flowId.HasValue) query = query.Where(e => e.FlowId == flowId);
        if (!string.IsNullOrWhiteSpace(dnis)) query = query.Where(e => e.Dnis == dnis);
        if (!string.IsNullOrWhiteSpace(ani)) query = query.Where(e => e.Ani == ani);

        return await query
            .OrderByDescending(e => e.EnteredAt)
            .Take(limit)
            .Select(e => new CallTraceCallSummary(
                e.CallRecordId, e.CampaignId, e.FlowId, e.Dnis, e.Ani, e.EnteredAt))
            .ToListAsync(ct);
    }
}
