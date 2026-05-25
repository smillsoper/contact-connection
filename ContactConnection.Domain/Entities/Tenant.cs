using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Domain.Entities;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string? LogoUrl { get; private set; }
    public string Subdomain { get; private set; } = string.Empty;
    public string? CustomDomain { get; private set; }
    public string SchemaName { get; private set; } = string.Empty;
    public string PlanTier { get; private set; } = string.Empty;
    public string Timezone { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset? TrialExpiresAt { get; private set; }
    public string? BillingContact { get; private set; }
    public string? InviteEmail { get; private set; }
    public TenantFeatureFlags FeatureFlags { get; private set; } = TenantFeatureFlags.Default();
    public TenantSettings Settings { get; private set; } = TenantSettings.Default();
    public bool OnboardingComplete { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Required by EF Core
    private Tenant() { }

    public static Tenant Create(
        string name,
        string subdomain,
        string timezone,
        TenantFeatureFlags? featureFlags = null,
        string? inviteEmail = null)
    {
        // Replace hyphens/spaces with underscores so the schema name is a valid unquoted PG identifier.
        var normalized = subdomain.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Subdomain = normalized,
            SchemaName = $"tenant_{normalized}",
            PlanTier = string.Empty,
            Timezone = timezone,
            IsActive = true,
            InviteEmail = inviteEmail,
            FeatureFlags = featureFlags ?? TenantFeatureFlags.Default(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void SetCustomDomain(string? customDomain) => CustomDomain = customDomain;
    public void SetBillingContact(string? billingContact) => BillingContact = billingContact;
    public void SetInviteEmail(string? email) => InviteEmail = email;
    public void SetTrialExpiry(DateTimeOffset? expiresAt) => TrialExpiresAt = expiresAt;
    public void SetDisplayName(string? displayName) => DisplayName = displayName;
    public void SetLogoUrl(string? logoUrl) => LogoUrl = logoUrl;
    public void UpdateSettings(TenantSettings settings) => Settings = settings;
    public void CompleteOnboarding() => OnboardingComplete = true;
    public void ResetOnboarding() => OnboardingComplete = false;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void UpdateFeatureFlags(TenantFeatureFlags flags) => FeatureFlags = flags;
}
