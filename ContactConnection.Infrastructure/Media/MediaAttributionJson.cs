using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Infrastructure.Media;

/// <summary>
/// The call's media attribution as the <c>call_record.media</c> object scripts and Liquid templates read
/// (S171): {{call_record.media.station}}, {{call_record.media.fields.access_code}}. Each agency field is
/// there under its own name (Liquid: call_record.media.fields["ACCESS CODE"]) and under a tag-safe alias —
/// lowercase, non-alphanumerics as "_" — because script tags can't contain spaces.
/// </summary>
public static partial class MediaAttributionJson
{
    public static JsonObject ToJson(MediaAttribution m)
    {
        var fields = new JsonObject();
        foreach (var (name, value) in m.Fields)
        {
            fields[name] = value;
            var alias = Alias(name);
            if (alias != name && !fields.ContainsKey(alias)) fields[alias] = value;
        }
        return new JsonObject
        {
            ["market_type"]  = m.MarketType,
            ["agency"]       = m.Agency,
            ["station"]      = m.Station,
            ["media_type"]   = m.MediaType ?? "",
            ["ad_type"]      = m.AdType ?? "",
            ["start_date"]   = m.StartDate.ToString("yyyy-MM-dd"),
            ["phone_number"] = m.PhoneNumber ?? "",
            ["fields"]       = fields,
        };
    }

    /// <summary>"ACCESS CODE" → "access_code", "Product-Code" → "product_code".</summary>
    public static string Alias(string name) => NonAlnum().Replace(name.Trim().ToLowerInvariant(), "_").Trim('_');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}
