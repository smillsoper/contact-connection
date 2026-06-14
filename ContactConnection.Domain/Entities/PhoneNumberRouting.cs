namespace ContactConnection.Domain.Entities;

/// <summary>
/// Global DID routing table — public schema, platform-wide.
/// Written to whenever a phone number is added, reassigned, activated, or deactivated
/// in any tenant schema. Enables DNIS-based tenant resolution for IP-routing carriers
/// (e.g. Commio) where no SIP credentials or subdomain are present on the inbound call.
///
/// E.164 numbers are globally unique — one DID can only be owned by one party at a time,
/// so there is no ambiguity when resolving a tenant from the called number alone.
/// </summary>
public class PhoneNumberRouting
{
    public Guid   Id         { get; private set; }
    public string Number     { get; private set; } = string.Empty;  // E.164: +15035551234
    public Guid   TenantId   { get; private set; }
    public Guid   CampaignId { get; private set; }
    public bool   IsActive   { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PhoneNumberRouting() { }

    public static PhoneNumberRouting Create(string number, Guid tenantId, Guid campaignId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PhoneNumberRouting
        {
            Id         = Guid.NewGuid(),
            Number     = number.Trim(),
            TenantId   = tenantId,
            CampaignId = campaignId,
            IsActive   = true,
            CreatedAt  = now,
            UpdatedAt  = now,
        };
    }

    public void Update(Guid campaignId)
    {
        CampaignId = campaignId;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}
