using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Payments;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

/// <summary>Campaign card data retention (S166): "until the order is submitted" keeps a captured card
/// past the script so a Call Records reviewer can re-authorize a corrected order.</summary>
public class CardDataRetentionServiceTests
{
    private readonly Mock<ICallRecordRepository> _records = new();
    private readonly Mock<ICampaignRepository> _campaigns = new();

    private CardDataRetentionService Service() => new(_records.Object, _campaigns.Object);

    private Campaign AddCampaign(string mode)
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "NeuroQ - LF TV", "neuroq-lf-tv");
        campaign.SetCardDataRetention(mode);
        _campaigns.Setup(c => c.GetByIdAsync(campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
        return campaign;
    }

    [Fact]
    public async Task Holds_OnlyForUntilOrderSubmittedCampaigns()
    {
        var holding = AddCampaign(CardDataRetentionMode.UntilOrderSubmitted);
        var normal = AddCampaign(CardDataRetentionMode.UntilScriptEnds);

        Assert.True(await Service().HoldsUntilOrderSubmittedAsync(holding.Id));
        Assert.False(await Service().HoldsUntilOrderSubmittedAsync(normal.Id));
        Assert.False(await Service().HoldsUntilOrderSubmittedAsync(Guid.Empty));      // no campaign → default wipe
        Assert.False(await Service().HoldsUntilOrderSubmittedAsync(Guid.NewGuid()));  // unknown campaign
    }

    [Fact]
    public async Task Release_WipesTheCard_WithOrderSubmittedReason_AndIsANoOpWhenNothingOnFile()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        record.StoreSensitiveData("ciphertext");
        _records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        await Service().ReleaseAfterOrderSubmittedAsync(record.Id);
        await Service().ReleaseAfterOrderSubmittedAsync(record.Id);

        Assert.Null(record.SensitiveData);
        Assert.Equal("order_submitted", record.SensitiveWipeReason);
        _records.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SetCardDataRetention_RejectsUnknownModes()
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "x", "x");
        Assert.Equal(CardDataRetentionMode.UntilScriptEnds, campaign.CardDataRetention);
        Assert.Throws<ArgumentException>(() => campaign.SetCardDataRetention("forever"));
    }
}
