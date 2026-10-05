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

    // Per-minute billing (S175): every billed carrier minute at the rate, toll-free minutes plus the surcharge,
    // raised to the monthly minimum. Null = not set yet (platform defaults apply).
    public decimal? BillingRatePerMinute { get; private set; }
    public decimal? BillingTollFreeSurcharge { get; private set; }
    public decimal? BillingMonthlyMinimum { get; private set; }

    public const decimal DefaultRatePerMinute = 0.035m;
    public const decimal DefaultTollFreeSurcharge = 0.01m;
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

    // ── Paying us (S179, Sprint 1 item 4) — Stripe ids and a display label only; card / bank numbers stay at Stripe. ──
    public string? StripeCustomerId { get; private set; }
    public string? PaymentMethodId { get; private set; }
    /// <summary>card | us_bank_account</summary>
    public string? PaymentMethodType { get; private set; }
    /// <summary>"Visa •••• 4242", "STRIPE TEST BANK •••• 6789" — for display.</summary>
    public string? PaymentMethodLabel { get; private set; }
    /// <summary>Charge the saved method automatically when an invoice is issued.</summary>
    public bool AutopayEnabled { get; private set; }

    public void SetStripeCustomer(string customerId) => StripeCustomerId = customerId;

    public void SetPaymentMethod(string paymentMethodId, string type, string label)
    {
        PaymentMethodId = paymentMethodId;
        PaymentMethodType = type;
        PaymentMethodLabel = label;
    }

    public void SetAutopay(bool enabled)
    {
        if (enabled && PaymentMethodId is null) throw new InvalidOperationException("Add a payment method before turning on autopay.");
        AutopayEnabled = enabled;
    }

    public void SetBillingRates(decimal ratePerMinute, decimal tollFreeSurcharge, decimal monthlyMinimum)
    {
        if (ratePerMinute < 0 || tollFreeSurcharge < 0 || monthlyMinimum < 0)
            throw new ArgumentException("Billing rates can't be negative.");
        BillingRatePerMinute = ratePerMinute;
        BillingTollFreeSurcharge = tollFreeSurcharge;
        BillingMonthlyMinimum = monthlyMinimum;
    }
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
