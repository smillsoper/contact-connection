namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Single recording hook for call queue/routing state transitions (pre_queue, in_queue,
/// routing, active, completed, abandoned). Assigns the next Sequence for the call via an
/// atomic Redis counter (mirrors ICallTraceRecorder) so callers never manage ordering.
/// </summary>
public interface ICallStateHistoryRecorder
{
    Task RecordAsync(
        Guid tenantId,
        string tenantSchemaName,
        Guid callRecordId,
        string state,
        Guid campaignId,
        Guid? agentId,
        string? detail,
        string? abandonType = null,
        string? abandonLength = null,
        bool? metServiceLevel = null,
        CancellationToken ct = default);
}
