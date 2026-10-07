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
    /// <summary>The campaign the number serves; null = in Reserve (S182) — owned by the tenant, waiting to be assigned,
    /// and its calls are rejected.</summary>
    public Guid? CampaignId { get; private set; }
    /// <summary>When the number went into Reserve — numbers are taken from Reserve oldest first, so drag calls from a
    /// recently retired campaign die down before the number is reused.</summary>
    public DateTimeOffset? ReservedAt { get; private set; }

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

    /// <param name="campaignId">null = straight into Reserve.</param>
    public static PhoneNumber Create(Guid tenantId, Guid? campaignId, string number, string? label = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PhoneNumber
        {
            Id         = Guid.NewGuid(),
            TenantId   = tenantId,
            CampaignId = campaignId,
            ReservedAt = campaignId is null ? now : null,
            Number     = Normalize(number),
            Label      = label?.Trim(),
            IsActive   = true,
            CreatedAt  = now,
            UpdatedAt  = now
        };
    }

    /// <summary>Onto a campaign (from Reserve or another campaign). Active / inactive is kept as it is; flow overrides
    /// chosen for the previous campaign are dropped so the number runs the new campaign's flows.</summary>
    public void Reassign(Guid campaignId)
    {
        if (CampaignId == campaignId) return;
        CampaignId      = campaignId;
        ReservedAt      = null;
        FlowId          = null;
        TelephonyFlowId = null;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    /// <summary>Back to Reserve: no campaign; the clock for "oldest first" starts now. The number stays the tenant's
    /// (active) — only deactivating it while in Reserve releases it.</summary>
    public void MoveToReserve()
    {
        if (CampaignId is null) return;
        CampaignId      = null;
        ReservedAt      = DateTimeOffset.UtcNow;
        FlowId          = null;
        TelephonyFlowId = null;
        IsActive        = true;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    public bool InReserve => CampaignId is null;

    /// <summary>
    /// Released: deactivated while in Reserve — the number no longer belongs to the tenant (another account may take it), but
    /// the row stays for its history. Inactive on a campaign is different: held for that campaign, still the tenant's.
    /// </summary>
    public bool IsReleased => CampaignId is null && !IsActive;

    /// <summary>Receives calls: active and on a campaign. Reserve, inactive and released numbers are rejected.</summary>
    public bool TakesCalls => IsActive && CampaignId is not null;

    /// <summary>E.164 for North American numbers typed with or without +1 / punctuation; anything else trimmed, with a +.</summary>
    public static string Normalize(string number)
    {
        var trimmed = number.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            10 => "+1" + digits,
            11 when digits[0] == '1' => "+" + digits,
            > 0 => "+" + digits,
            _ => trimmed,
        };
    }

    /// <summary>The forms a carrier may present the same number in (with / without + and the leading 1).</summary>
    public static string[] Forms(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        var last10 = digits.Length >= 10 ? digits[^10..] : digits;
        return [number.Trim(), digits, "+" + digits, last10, "1" + last10, "+1" + last10];
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
