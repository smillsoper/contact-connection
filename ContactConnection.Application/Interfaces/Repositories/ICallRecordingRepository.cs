using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Schema-scoped persistence for call-recording lifecycle events. Separate from
/// <see cref="ICallRecordRepository"/> (which resolves the tenant from the ambient request
/// context) because recording events are appended from the ESL background context, where the
/// tenant schema must be passed explicitly — same pattern as ICallStateHistoryRepository /
/// ICallTraceEventRepository.
/// </summary>
public interface ICallRecordingRepository
{
    /// <summary>
    /// Loads the call record in <paramref name="tenantSchemaName"/>, appends <paramref name="evt"/>
    /// (which also refreshes the denormalised recording_* columns), and saves. Writes for one
    /// call record are serialised so concurrent mask/stop events can't lose an update.
    /// Returns false if no such call record exists.
    /// </summary>
    Task<bool> AppendEventAsync(
        string tenantSchemaName, Guid callRecordId, RecordingEvent evt, CancellationToken ct = default);
}
