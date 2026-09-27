namespace ContactConnection.Domain.Entities;

/// <summary>ITaxProvider dispatch keys a campaign can select (Campaign.TaxProvider).</summary>
public static class TaxProviderKey
{
    /// <summary>Flat percentage from the campaign's settings ({"rate":0.0725}); the default.</summary>
    public const string FlatRate = "";
    public const string Avalara  = "avalara";

    private static readonly HashSet<string> _all = [FlatRate, Avalara];

    public static bool IsValid(string key) => _all.Contains(key);
    public static IReadOnlyCollection<string> All => _all;
}

/// <summary>Outcome of the most recent tax calculation on a cart (CartDocument.TaxStatus).</summary>
public static class TaxCalculationStatus
{
    public const string Calculated     = "calculated";
    /// <summary>The provider needs an address the call doesn't have yet — tax is 0 until it does.</summary>
    public const string PendingAddress = "pending_address";
    /// <summary>The provider failed (bad credentials, outage, rejected address) — tax is 0 and
    /// TaxMessage says why, so the agent sees it rather than silently quoting a tax-free total.</summary>
    public const string Error          = "error";
}
