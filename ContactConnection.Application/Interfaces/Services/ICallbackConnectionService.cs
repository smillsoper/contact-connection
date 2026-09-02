namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Resolves a fired <c>Callback</c>'s outcome from the telephony ESL path — schema-explicit
/// (no ambient <c>TenantContext</c>), mirroring <see cref="ICallStateHistoryRecorder"/>. The
/// Worker's <c>CallbackProcessingService</c> places the outbound leg and marks the row
/// <c>attempted</c>; these two methods land the terminal state once FreeSWITCH reports what
/// happened to that leg.
/// </summary>
public interface ICallbackConnectionService
{
    /// <summary>The callback leg answered and re-entered the campaign flow. Moves an
    /// <c>attempted</c> callback to <c>completed</c> and links the connected call record.
    /// Returns false (no-op) if the row is missing or not <c>attempted</c> (already resolved).</summary>
    Task<bool> MarkConnectedAsync(
        string tenantSchemaName, Guid tenantId, Guid callbackId, Guid connectedCallRecordId,
        CancellationToken ct = default);

    /// <summary>The callback leg did not connect (no answer / busy / failed). Runs
    /// <c>Callback.MarkNoAnswer</c> — back to <c>scheduled</c> while retries remain, else
    /// <c>abandoned</c> plus a <c>callback_abandon</c> call-state-history row against the
    /// original inbound call record. Returns true when it abandoned.</summary>
    Task<bool> MarkNoAnswerAsync(
        string tenantSchemaName, Guid tenantId, Guid callbackId, string? cause,
        CancellationToken ct = default);
}
