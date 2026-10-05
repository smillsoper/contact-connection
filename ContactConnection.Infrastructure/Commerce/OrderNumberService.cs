using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>See IOrderNumberService.</summary>
public class OrderNumberService(IOrderNumberSequenceRepository sequences) : IOrderNumberService
{
    public async Task<string?> GetOrAssignAsync(CallRecord record, CallInteraction? interaction, CancellationToken ct = default)
    {
        if (interaction is null) return await GetOrAssignAsync(record, ct);
        if (!string.IsNullOrEmpty(interaction.OrderNumber)) return interaction.OrderNumber;

        var mirror = record.MirrorsCommerceOf(interaction);
        string? number;
        if (mirror && !string.IsNullOrEmpty(record.OrderNumber))
            number = await sequences.AssignToInteractionAsync(interaction.Id, record.OrderNumber, ct);
        else
        {
            if (record.ClientId == Guid.Empty) return null;
            var allocated = await sequences.AllocateAsync(record.ClientId, ct);
            if (allocated is null) return null;
            number = await sequences.AssignToInteractionAsync(interaction.Id, allocated, ct);
            if (mirror && number is not null) await sequences.AssignToCallRecordAsync(record.Id, number, ct);
        }

        if (number is not null) interaction.SetOrderNumber(number);
        return number;
    }

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
