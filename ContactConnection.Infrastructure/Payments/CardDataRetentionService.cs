using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Payments;

/// <summary>See ICardDataRetentionService.</summary>
public class CardDataRetentionService(ICallRecordRepository callRecords, ICampaignRepository campaigns)
    : ICardDataRetentionService
{
    public const string OrderSubmittedReason = "order_submitted";

    public async Task<bool> HoldsUntilOrderSubmittedAsync(Guid campaignId, CancellationToken ct = default)
    {
        if (campaignId == Guid.Empty) return false;
        var campaign = await campaigns.GetByIdAsync(campaignId, ct);
        return campaign?.CardDataRetention == CardDataRetentionMode.UntilOrderSubmitted;
    }

    public async Task ReleaseAfterOrderSubmittedAsync(Guid callRecordId, CancellationToken ct = default)
    {
        var record = await callRecords.GetByIdAsync(callRecordId, ct);
        if (record is null || string.IsNullOrEmpty(record.SensitiveData)) return;
        record.WipeSensitiveData(OrderSubmittedReason);
        await callRecords.SaveChangesAsync(ct);
    }
}
