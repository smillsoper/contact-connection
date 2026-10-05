namespace ContactConnection.Domain.ValueObjects;

public class TenantSettings
{
    public string DateFormat { get; set; } = "MM/DD/YYYY";
    public string TimeFormat { get; set; } = "12h";
    public string? SupportEmail { get; set; }
    public string? BillingEmail { get; set; }
    public int SessionTimeoutMinutes { get; set; } = 480;
    public string MfaRequirement { get; set; } = "off";

    /// <summary>Commission pay periods (S171): weekly | biweekly | semimonthly | monthly.</summary>
    public string PayPeriodFrequency { get; set; } = PayPeriods.Biweekly;
    /// <summary>First day of any pay period (yyyy-MM-dd) — anchors weekly/biweekly periods. Null = 2026-01-05 (a Monday).</summary>
    public string? PayPeriodStart { get; set; }

    /// <summary>Caller ID (E.164) for direct dials and campaigns with none of their own (S179, manual outbound).</summary>
    public string? DefaultOutboundCallerId { get; set; }

    /// <summary>A copy — settings are replaced whole (Tenant.UpdateSettings), so writers start from this, not a new object.</summary>
    public TenantSettings Clone() => (TenantSettings)MemberwiseClone();

    public static TenantSettings Default() => new();
}
