namespace ContactConnection.Domain.Entities;

public class TenantFeatureFlags
{
    public bool Telephony { get; set; }
    public bool OmsBuiltIn { get; set; }
    public bool ShopifyAdapter { get; set; }
    public bool TenantChat { get; set; }
    /// <summary>
    /// Data exports may include card data (S182) — for campaigns whose orders go out only as files to a fulfillment center
    /// that runs the cards itself. Switched on by the platform in the Portal (never self-service), after the client's PCI
    /// paperwork is in place.
    /// </summary>
    public bool CardDataExports { get; set; }

    public static TenantFeatureFlags Default() => new()
    {
        Telephony = false,
        OmsBuiltIn = false,
        ShopifyAdapter = false,
        TenantChat = false,
    };
}
