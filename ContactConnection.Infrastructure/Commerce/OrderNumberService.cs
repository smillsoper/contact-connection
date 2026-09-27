using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>See IOrderNumberService.</summary>
public class OrderNumberService(IOrderNumberSequenceRepository sequences) : IOrderNumberService
{
    public async Task<string?> GetOrAssignAsync(CallRecord record, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(record.OrderNumber)) return record.OrderNumber;
        if (record.ClientId == Guid.Empty) return null;

        var allocated = await sequences.AllocateAsync(record.ClientId, ct);
        if (allocated is null) return null;

        // Conditional write — if a concurrent first-use already stamped a number, that one wins and
        // is returned instead (see IOrderNumberSequenceRepository.AssignToCallRecordAsync).
        return await sequences.AssignToCallRecordAsync(record.Id, allocated, ct);
    }
}
