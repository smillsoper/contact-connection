namespace ContactConnection.Domain.Entities;

/// <summary>
/// Stores a tenant's choice of which endpoint to use for a given API sub-type — either a
/// platform-catalog PortalApiEndpoint, or the tenant's own TenantApiEndpoint (e.g. because they
/// manage their own subscription/credentials for that vendor rather than sharing a platform-wide
/// one — a tenant paying for their own zip-codes.com account rather than the platform footing
/// every tenant's usage). One record per sub-type per tenant. Absence of a record means use the
/// platform-preferred default for that sub-type.
/// </summary>
public class TenantApiPreference
{
    public Guid Id { get; private set; }
    public string ApiSubType { get; private set; } = string.Empty;
    /// <summary>ApiPreferenceSource.Portal or .Tenant — which table EndpointId refers to.</summary>
    public string Source { get; private set; } = ApiPreferenceSource.Portal;
    /// <summary>
    /// Cross-schema reference — portal_api_endpoints.id when Source=Portal, or this tenant's own
    /// tenant_api_endpoints.id when Source=Tenant. No DB FK constraint either way (portal is a
    /// different schema; tenant endpoints could in principle be deleted out from under a
    /// preference — callers should treat a missing lookup as "preference needs re-selecting").
    /// </summary>
    public Guid EndpointId { get; private set; }
    // Tenant-specific parameter overrides for the chosen endpoint — e.g. for TtsStreaming,
    // the tenant's chosen voice id / style / stability (the "mapping tab" values). Distinct
    // from credentials (which live in ITenantCredentialStore, never here). Null/"{}" for
    // sub-types that don't need per-tenant parameterization beyond "which endpoint".
    public string? SettingsJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private TenantApiPreference() { }

    public static TenantApiPreference Create(string apiSubType, string source, Guid endpointId)
    {
        if (!Entities.ApiSubType.IsValid(apiSubType))
            throw new ArgumentException($"Unknown API sub-type '{apiSubType}'.", nameof(apiSubType));
        if (!ApiPreferenceSource.IsValid(source))
            throw new ArgumentException($"Unknown preference source '{source}'.", nameof(source));

        return new TenantApiPreference
        {
            Id = Guid.NewGuid(),
            ApiSubType = apiSubType,
            Source = source,
            EndpointId = endpointId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void SetEndpoint(string source, Guid endpointId)
    {
        if (!ApiPreferenceSource.IsValid(source))
            throw new ArgumentException($"Unknown preference source '{source}'.", nameof(source));
        Source = source;
        EndpointId = endpointId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetSettings(string? settingsJson)
    {
        SettingsJson = settingsJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
