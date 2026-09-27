namespace ContactConnection.Domain.Entities;

/// <summary>
/// A DID (Direct Inward Dialing) number assigned to a campaign.
/// Lives in the tenant schema — tenant identity is resolved first via the SIP gateway
/// that the call arrives on, so multiple tenants can share the same DID without conflict.
/// </summary>
public class PhoneNumber
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CampaignId { get; private set; }

    public string Number { get; private set; } = string.Empty;   // E.164: +15035551234
    public string? Label { get; private set; }                    // human-readable name
    public bool IsActive { get; private set; }

    // DID-level CRM script flow override. Falls back to Campaign.FlowId if null.
    public Guid? FlowId { get; private set; }

    // DID-level telephony flow override. Falls back to Campaign.InboundFlowId if null.
    public Guid? TelephonyFlowId { get; private set; }

    // Who houses this number (NumberProvider) and what the row represents (PhoneNumberRole). For a
    // routing-platform delivery number (pseudo-DNIS), ClientNumber is the public number the caller
    // actually dialed — housed at the routing platform (e.g. RingSquared's client TFNs).
    public Guid? ProviderId { get; private set; }
    public string Role { get; private set; } = PhoneNumberRole.Hosted;
    public string? ClientNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public Campaign? Campaign { get; private set; }

    private PhoneNumber() { }

    public static PhoneNumber Create(Guid tenantId, Guid campaignId, string number, string? label = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PhoneNumber
        {
            Id         = Guid.NewGuid(),
            TenantId   = tenantId,
            CampaignId = campaignId,
            Number     = number.Trim(),
            Label      = label?.Trim(),
            IsActive   = true,
            CreatedAt  = now,
            UpdatedAt  = now
        };
    }

    public void Reassign(Guid campaignId)
    {
        CampaignId = campaignId;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void UpdateLabel(string? label)
    {
        Label     = label?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Sets who houses the number and its role. A routing-delivery number must name the
    /// public client number it stands for; a hosted number never has one.</summary>
    public void SetProvider(Guid? providerId, string role, string? clientNumber)
    {
        if (!PhoneNumberRole.IsValid(role)) throw new ArgumentException($"Unknown number role '{role}'.", nameof(role));
        var client = string.IsNullOrWhiteSpace(clientNumber) ? null : clientNumber.Trim();
        if (role == PhoneNumberRole.RoutingDelivery && client is null)
            throw new ArgumentException("A routing delivery number needs the client number it stands for.", nameof(clientNumber));
        ProviderId   = providerId;
        Role         = role;
        ClientNumber = role == PhoneNumberRole.RoutingDelivery ? client : null;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void AssignFlow(Guid flowId)           { FlowId          = flowId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveFlow()                      { FlowId          = null;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void AssignTelephonyFlow(Guid flowId)  { TelephonyFlowId = flowId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveTelephonyFlow()             { TelephonyFlowId = null;   UpdatedAt = DateTimeOffset.UtcNow; }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}
