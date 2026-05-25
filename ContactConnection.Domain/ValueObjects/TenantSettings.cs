namespace ContactConnection.Domain.ValueObjects;

public class TenantSettings
{
    public string DateFormat { get; set; } = "MM/DD/YYYY";
    public string TimeFormat { get; set; } = "12h";
    public string? SupportEmail { get; set; }
    public string? BillingEmail { get; set; }
    public int SessionTimeoutMinutes { get; set; } = 480;
    public string MfaRequirement { get; set; } = "off";

    public static TenantSettings Default() => new();
}
