using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>See IOrderNumberService.</summary>
public class OrderNumberService(IOrderNumberSequenceRepository sequences) : IOrderNumberService
{
    /// <summary>The interaction's order number (S178) — assigned once from the client's sequence with a conditional write,
    /// so two concurrent first-uses can't give one interaction two numbers. Null without an interaction, a client, or a
    /// sequence.</summary>
    public async Task<string?> GetOrAssignAsync(CallRecord record, CallInteraction? interaction, CancellationToken ct = default)
    {
        if (interaction is null) return null;
        if (!string.IsNullOrEmpty(interaction.OrderNumber)) return interaction.OrderNumber;
        if (record.ClientId == Guid.Empty) return null;

        var allocated = await sequences.AllocateAsync(record.ClientId, ct);
        if (allocated is null) return null;

        var number = await sequences.AssignToInteractionAsync(interaction.Id, allocated, ct);
        if (number is not null) interaction.SetOrderNumber(number);
        return number;
    }
}
