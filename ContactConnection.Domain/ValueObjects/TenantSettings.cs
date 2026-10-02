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

    public static TenantSettings Default() => new();
}
