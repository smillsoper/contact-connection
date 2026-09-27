using System.Text.Json.Nodes;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Infrastructure.FlowEngine;

/// <summary>
/// Converts between the address node's output object (what {{flow.billing_address}} holds —
/// firstName, address1, city, isPOBox, ...) and the call record's AddressData. Both directions use
/// the same property names, so {{call_record.shipping_address.city}} reads exactly like
/// {{flow.billing_address.city}}, and set_variable can copy one into the other.
/// </summary>
public static class CallAddressJson
{
    /// <summary>Address-node-shaped JSON → AddressData. Returns null when the value isn't a JSON
    /// object or has no ZIP, street or city (i.e. isn't recognizably an address).</summary>
    public static AddressData? ToAddressData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) is JsonObject o ? ToAddressData(o) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static AddressData? ToAddressData(JsonObject o)
    {
        string? S(string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
        bool B(string key) => o[key] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b
            : v.TryGetValue<string>(out var s) && bool.TryParse(s, out var p) && p);
        double? D(string key) => o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

        var address = new AddressData
        {
            FirstName     = S("firstName"),
            MiddleInitial = S("middleInitial"),
            LastName      = S("lastName"),
            Company       = S("company"),
            Prefix        = S("address1Prefix"),
            Street        = S("address1"),
            UnitPrefix    = S("address2Prefix"),
            Unit          = S("address2"),
            City          = S("city"),
            State         = S("state")?.ToUpperInvariant(),
            Zip           = S("zip"),
            Zip4          = S("zip4"),
            Country       = S("country"),
            IsPOBox       = B("isPOBox"),
            IsCanada      = B("isCanada"),
            IsForeign     = B("isForeign"),
            IsMilitary    = B("isMilitary"),
            IsOutlyingUS  = B("isOutlyingUS"),
            IsAKHI        = B("isAKHI"),
            IsVerified    = B("isVerified"),
            Latitude      = D("latitude"),
            Longitude     = D("longitude"),
        };
        return address.Zip is null && address.Street is null && address.City is null ? null : address;
    }

    /// <summary>AddressData → address-node-shaped JSON (including the formatted fields).</summary>
    public static JsonObject ToJsonObject(AddressData a)
    {
        static string T(string? s) => s ?? string.Empty;
        var fmt1 = string.Join(" ", new[] { a.Prefix, a.Street }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var fmt2 = string.Join(" ", new[] { a.UnitPrefix, a.Unit }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var zipFull = string.IsNullOrWhiteSpace(a.Zip4) ? T(a.Zip) : $"{a.Zip}-{a.Zip4}";
        var stateZip = string.Join(" ", new[] { a.State, zipFull }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var cityStateZip = string.Join(", ", new[] { a.City, stateZip }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var country = string.IsNullOrWhiteSpace(a.Country) ? "US" : a.Country;
        var lines = new[] { fmt1, fmt2, cityStateZip, country == "US" ? "" : country }.Where(s => s.Length > 0);

        var o = new JsonObject
        {
            ["firstName"]         = T(a.FirstName),
            ["middleInitial"]     = T(a.MiddleInitial),
            ["lastName"]          = T(a.LastName),
            ["company"]           = T(a.Company),
            ["address1Prefix"]    = T(a.Prefix),
            ["address1"]          = T(a.Street),
            ["address2Prefix"]    = T(a.UnitPrefix),
            ["address2"]          = T(a.Unit),
            ["formattedAddress1"] = fmt1,
            ["formattedAddress2"] = fmt2,
            ["fullAddress"]       = string.Join(", ", lines),
            ["city"]              = T(a.City),
            ["state"]             = T(a.State),
            ["zip"]               = T(a.Zip),
            ["zip4"]              = T(a.Zip4),
            ["country"]           = country,
            ["isPOBox"]           = a.IsPOBox,
            ["isCanada"]          = a.IsCanada,
            ["isMilitary"]        = a.IsMilitary,
            ["isOutlyingUS"]      = a.IsOutlyingUS,
            ["isForeign"]         = a.IsForeign,
            ["isAKHI"]            = a.IsAKHI,
            ["isVerified"]        = a.IsVerified,
        };
        if (a.Latitude.HasValue)  o["latitude"]  = a.Latitude.Value;
        if (a.Longitude.HasValue) o["longitude"] = a.Longitude.Value;
        return o;
    }
}
