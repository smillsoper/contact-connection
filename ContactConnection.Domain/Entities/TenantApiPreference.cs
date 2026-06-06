namespace ContactConnection.Domain.Entities;

/// <summary>
/// Stores a tenant's choice to use a specific portal endpoint for a given API sub-type,
/// overriding the platform-preferred default. One record per sub-type per tenant.
/// Absence of a record means use the platform preferred endpoint.
/// </summary>
public class TenantApiPreference
{
    public Guid Id { get; private set; }
    public string ApiSubType { get; private set; } = string.Empty;
    // Cross-schema reference to portal_api_endpoints — no DB FK constraint
    public Guid PortalApiEndpointId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private TenantApiPreference() { }

    public static TenantApiPreference Create(string apiSubType, Guid portalApiEndpointId)
    {
        if (!Entities.ApiSubType.IsValid(apiSubType))
            throw new ArgumentException($"Unknown API sub-type '{apiSubType}'.", nameof(apiSubType));

        return new TenantApiPreference
        {
            Id = Guid.NewGuid(),
            ApiSubType = apiSubType,
            PortalApiEndpointId = portalApiEndpointId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void SetEndpoint(Guid portalApiEndpointId)
    {
        PortalApiEndpointId = portalApiEndpointId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
