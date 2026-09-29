using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>Append-only post-call change log — see CallRecordAuditEntry.</summary>
public interface ICallRecordAuditRepository
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<CallRecordAuditEntry>> GetByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Adds and saves in one step — an audit row is never left pending.</summary>
    Task AddAsync(CallRecordAuditEntry entry, CancellationToken ct = default);
}
