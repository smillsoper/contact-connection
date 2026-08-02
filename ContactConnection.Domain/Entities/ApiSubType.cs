namespace ContactConnection.Domain.Entities;

public static class ApiSubType
{
    // Address category
    public const string AddressValidation           = "address_validation";
    public const string ZipCodeLookup               = "zip_code_lookup";
    public const string RealtimeAddressAutocomplete = "realtime_address_autocomplete";

    // Order category
    public const string OrderSubmit = "order_submit";
    public const string OrderLookup = "order_lookup";
    public const string OrderCancel = "order_cancel";

    // Fulfillment category
    public const string FulfillmentTracking = "fulfillment_tracking";
    public const string FulfillmentCancel   = "fulfillment_cancel";
    public const string FulfillmentReturn   = "fulfillment_return";
    public const string FulfillmentSubmit   = "fulfillment_submit";

    // Media category (DR/Print media agency APIs)
    public const string TfnAssignmentAdd    = "tfn_assignment_add";
    public const string TfnAssignmentUpdate = "tfn_assignment_update";
    public const string TfnAssignmentDelete = "tfn_assignment_delete";
    public const string CampaignResults     = "campaign_results";

    // Media category (telephony audio) — one sub-type shared by every streaming TTS vendor
    // (Azure, ElevenLabs, etc.); vendor identity lives on the PortalApiDefinition's Provider
    // field, not as separate sub-types per vendor. See ITtsStreamProvider.
    public const string TtsStreaming = "tts_streaming";

    private static readonly Dictionary<string, string> _categoryMap = new()
    {
        [AddressValidation]           = ApiCategory.Address,
        [ZipCodeLookup]               = ApiCategory.Address,
        [RealtimeAddressAutocomplete] = ApiCategory.Address,
        [OrderSubmit]                 = ApiCategory.Order,
        [OrderLookup]                 = ApiCategory.Order,
        [OrderCancel]                 = ApiCategory.Order,
        [FulfillmentTracking]         = ApiCategory.Fulfillment,
        [FulfillmentCancel]           = ApiCategory.Fulfillment,
        [FulfillmentReturn]           = ApiCategory.Fulfillment,
        [FulfillmentSubmit]           = ApiCategory.Fulfillment,
        [TfnAssignmentAdd]            = ApiCategory.Media,
        [TfnAssignmentUpdate]         = ApiCategory.Media,
        [TfnAssignmentDelete]         = ApiCategory.Media,
        [CampaignResults]             = ApiCategory.Media,
        [TtsStreaming]                = ApiCategory.Media,
    };

    public static bool IsValid(string subType) => _categoryMap.ContainsKey(subType);

    public static string? CategoryOf(string subType) =>
        _categoryMap.TryGetValue(subType, out var cat) ? cat : null;

    public static IReadOnlyCollection<string> All => _categoryMap.Keys;

    public static IReadOnlyCollection<string> ForCategory(string category) =>
        _categoryMap.Where(kv => kv.Value == category).Select(kv => kv.Key).ToList();
}
