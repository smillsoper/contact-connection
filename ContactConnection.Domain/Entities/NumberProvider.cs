namespace ContactConnection.Domain.Entities;

/// <summary>
/// Who houses a phone number. A <b>carrier</b> (Telnyx, Bandwidth) hosts the number itself and hands
/// the call to us; a <b>routing platform</b> (e.g. RingSquared) houses the public number and delivers
/// its calls to us on a delivery number of ours (a pseudo-DNIS — see PhoneNumber.Role /
/// ClientNumber). CXone had no such concept (numbers could only be told apart by description).
///
/// A routing platform may also call our external routing endpoints (availability / routing
/// decisions) — authenticated by an API key issued to it here; only a SHA-256 hash is stored, the
/// plaintext is shown once when issued.
/// </summary>
public class NumberProvider
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = NumberProviderType.Carrier;
    /// <summary>The SIP gateway calls from this provider arrive on, when known.</summary>
    public Guid? SipGatewayId { get; private set; }
    /// <summary>Comma-separated source IPs/CIDRs this provider signals from (informational until the
    /// SIP ingress enforces it).</summary>
    public string? SourceIps { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    // External routing API access (routing platforms)
    public string? ApiKeyHash { get; private set; }
    /// <summary>First characters of the issued key, for recognizing it in the UI/logs.</summary>
    public string? ApiKeyPrefix { get; private set; }
    public DateTimeOffset? ApiKeyIssuedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private NumberProvider() { }

    public static NumberProvider Create(Guid tenantId, string name, string type, string? notes = null)
    {
        Validate(name, type);
        var now = DateTimeOffset.UtcNow;
        return new NumberProvider
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Type = type,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string type, Guid? sipGatewayId, string? sourceIps, string? notes)
    {
        Validate(name, type);
        Name = name.Trim();
        Type = type;
        SipGatewayId = sipGatewayId;
        SourceIps = string.IsNullOrWhiteSpace(sourceIps) ? null : sourceIps.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records a newly issued API key (hash + display prefix); replaces any previous key.</summary>
    public void SetApiKey(string hash, string prefix)
    {
        ApiKeyHash = hash;
        ApiKeyPrefix = prefix;
        ApiKeyIssuedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RevokeApiKey()
    {
        ApiKeyHash = null;
        ApiKeyPrefix = null;
        ApiKeyIssuedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }

    private static void Validate(string name, string type)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Provider name is required.", nameof(name));
        if (!NumberProviderType.IsValid(type)) throw new ArgumentException($"Unknown provider type '{type}'.", nameof(type));
    }
}

public static class NumberProviderType
{
    /// <summary>Hosts numbers and delivers their calls (Telnyx, Bandwidth).</summary>
    public const string Carrier = "carrier";
    /// <summary>Houses the public numbers and routes calls to us (RingSquared).</summary>
    public const string RoutingPlatform = "routing_platform";

    public static bool IsValid(string type) => type is Carrier or RoutingPlatform;
}

/// <summary>What a PhoneNumber row represents.</summary>
public static class PhoneNumberRole
{
    /// <summary>A real number hosted by our carrier — callers dial it directly.</summary>
    public const string Hosted = "hosted";
    /// <summary>A delivery number (pseudo-DNIS) a routing platform sends calls to; the public number
    /// the caller actually dialed is PhoneNumber.ClientNumber, housed at the routing platform.</summary>
    public const string RoutingDelivery = "routing_delivery";

    public static bool IsValid(string role) => role is Hosted or RoutingDelivery;
}
