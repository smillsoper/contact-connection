namespace ContactConnection.Domain.Entities;

/// <summary>
/// FreeSWITCH SIP gateway configuration for a tenant.
/// One gateway = one SIP trunk. The gateway name is what FreeSWITCH sets on
/// variable_sip_gateway_name at call arrival, giving us tenant identity before
/// any DNIS lookup. Stored in the public schema (platform-level) because the
/// ESL service must resolve tenant from the gateway name before any tenant
/// schema is selected.
/// </summary>
public class SipGateway
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    // FreeSWITCH gateway name — unique platform-wide, used as the lookup key on CHANNEL_PARK.
    public string Name { get; private set; } = string.Empty;

    public string Proxy { get; private set; } = string.Empty;      // SIP provider address / domain
    public string? FromDomain { get; private set; }                 // From header override (defaults to Proxy)
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;   // TODO: AES-256 encrypt at rest
    public bool Register { get; private set; }                      // Whether to register with provider
    public string Transport { get; private set; } = SipTransport.Udp;
    public string? CodecPrefs { get; private set; }                 // null = use FreeSWITCH defaults
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SipGateway() { }

    public static SipGateway Create(
        Guid tenantId,
        string name,
        string proxy,
        string username,
        string password,
        bool register = true,
        string transport = SipTransport.Udp,
        string? fromDomain = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SipGateway
        {
            Id         = Guid.NewGuid(),
            TenantId   = tenantId,
            Name       = name.Trim(),
            Proxy      = proxy.Trim(),
            FromDomain = fromDomain?.Trim(),
            Username   = username.Trim(),
            Password   = password,
            Register   = register,
            Transport  = transport,
            IsActive   = true,
            CreatedAt  = now,
            UpdatedAt  = now
        };
    }

    public void Update(string proxy, string username, string password,
        bool register, string transport, string? fromDomain, string? codecPrefs)
    {
        Proxy      = proxy.Trim();
        FromDomain = fromDomain?.Trim();
        Username   = username.Trim();
        Password   = password;
        Register   = register;
        Transport  = transport;
        CodecPrefs = codecPrefs?.Trim();
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}

public static class SipTransport
{
    public const string Udp = "udp";
    public const string Tcp = "tcp";
    public const string Tls = "tls";
}
