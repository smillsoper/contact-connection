namespace ContactConnection.Domain.Entities;

public class TenantFeatureFlags
{
    public bool Telephony { get; set; }
    public bool OmsBuiltIn { get; set; }
    public bool ShopifyAdapter { get; set; }
    public bool TenantChat { get; set; }

    public static TenantFeatureFlags Default() => new()
    {
        Telephony = false,
        OmsBuiltIn = false,
        ShopifyAdapter = false,
        TenantChat = false,
    };
}
