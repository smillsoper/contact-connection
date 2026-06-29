namespace ContactConnection.Domain.Entities;

public class CampaignExternalNumber
{
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    // Navigation
    public Campaign? Campaign { get; private set; }

    private CampaignExternalNumber() { }

    public static CampaignExternalNumber Create(
        Guid campaignId, Guid tenantId, string label, string number, int displayOrder = 0) =>
        new()
        {
            Id           = Guid.NewGuid(),
            CampaignId   = campaignId,
            TenantId     = tenantId,
            Label        = label.Trim(),
            Number       = number.Trim(),
            DisplayOrder = displayOrder,
            IsActive     = true,
            CreatedAt    = DateTimeOffset.UtcNow,
        };

    public void Deactivate() => IsActive = false;
}
