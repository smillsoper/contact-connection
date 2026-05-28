namespace ContactConnection.Domain.Entities;

public static class ApiDefinitionType
{
    public const string AddressValidation         = "address_validation";
    public const string RealtimeAddressAutocomplete = "realtime_address_autocomplete";
    public const string ZipCodeLookup             = "zip_code_lookup";
    public const string Fulfillment               = "fulfillment";

    private static readonly HashSet<string> _all =
    [
        AddressValidation,
        RealtimeAddressAutocomplete,
        ZipCodeLookup,
        Fulfillment,
    ];

    public static bool IsValid(string type) => _all.Contains(type);

    public static IReadOnlyCollection<string> All => _all;
}
