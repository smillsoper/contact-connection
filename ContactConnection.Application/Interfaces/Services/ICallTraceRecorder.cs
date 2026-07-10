namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Single recording hook called by both flow engines after every node execution.
/// Persists the step unconditionally (durability, independent of whether any live
/// trace is watching), then fans out to any matching live subscriptions.
/// </summary>
public interface ICallTraceRecorder
{
    Task RecordStepAsync(
        Guid tenantId,
        string tenantSchemaName,
        Guid callRecordId,
        string engine,
        string nodeId,
        string nodeType,
        string? label,
        string? detail,
        string? transitionTaken,
        string? nextNodeId,
        string? exitReason,
        Guid? campaignId,
        Guid? flowId,
        string? dnis,
        string? ani,
        CancellationToken ct = default);
}
