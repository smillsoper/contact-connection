namespace ContactConnection.Domain.Entities;

public static class ApiCategory
{
    public const string Address     = "address";
    public const string Order       = "order";
    public const string Fulfillment = "fulfillment";
    public const string Media       = "media";
    public const string General     = "general";

    private static readonly HashSet<string> _all =
    [
        Address,
        Order,
        Fulfillment,
        Media,
        General,
    ];

    public static bool IsValid(string category) => _all.Contains(category);

    public static IReadOnlyCollection<string> All => _all;
}
