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
        // S182: a campaign whose card data goes out in an export keeps it past the script too.
        return CardDataRetentionMode.KeepsPastScript(campaign?.CardDataRetention);
    }

    /// <summary>True when the campaign's card data waits for a card-data export (S182) — nothing else may wipe it early.</summary>
    public async Task<bool> HoldsForExportAsync(Guid campaignId, CancellationToken ct = default) =>
        campaignId != Guid.Empty && (await campaigns.GetByIdAsync(campaignId, ct))?.CardDataRetention == CardDataRetentionMode.UntilExported;

    public async Task ReleaseAfterOrderSubmittedAsync(Guid callRecordId, CancellationToken ct = default)
    {
        var record = await callRecords.GetByIdAsync(callRecordId, ct);
        if (record is null || string.IsNullOrEmpty(record.SensitiveData)) return;
        if (await HoldsForExportAsync(record.CampaignId, ct)) return;   // the export wipes it once delivered
        record.WipeSensitiveData(OrderSubmittedReason);
        await callRecords.SaveChangesAsync(ct);
    }
}
