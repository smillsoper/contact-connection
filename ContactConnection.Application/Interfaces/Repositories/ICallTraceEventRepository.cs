using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Every method takes an explicit tenant schema name rather than resolving it from
/// ambient TenantContext — CallTraceRecorder is called from both HTTP-scoped code
/// (TenantContext populated by middleware) and the ESL background service (no HTTP
/// request, so TenantContext is never populated). Explicit schema keeps this repository
/// correct in both call contexts.
/// </summary>
public interface ICallTraceEventRepository
{
    Task AddAsync(CallTraceEvent trace, string tenantSchemaName, CancellationToken ct = default);

    /// <summary>Full ordered timeline for one call — spans both telephony and CRM engines.</summary>
    Task<List<CallTraceEvent>> GetByCallRecordIdAsync(Guid callRecordId, string tenantSchemaName, CancellationToken ct = default);

    /// <summary>
    /// Distinct calls (most recent first-row per CallRecordId) matching the given filter —
    /// used to browse/reopen a past trace. Any filter left null matches all.
    /// </summary>
    Task<List<CallTraceCallSummary>> SearchCallsAsync(
        Guid tenantId,
        string tenantSchemaName,
        Guid? campaignId,
        Guid? flowId,
        string? dnis,
        string? ani,
        int limit,
        CancellationToken ct = default);
}

public record CallTraceCallSummary(
    Guid CallRecordId,
    Guid? CampaignId,
    Guid? FlowId,
    string? Dnis,
    string? Ani,
    DateTimeOffset StartedAt);
