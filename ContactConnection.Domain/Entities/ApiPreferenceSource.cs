namespace ContactConnection.Domain.Entities;

/// <summary>Which table TenantApiPreference.EndpointId references.</summary>
public static class ApiPreferenceSource
{
    public const string Portal = "portal";
    public const string Tenant = "tenant";

    public static bool IsValid(string source) => source is Portal or Tenant;
}
