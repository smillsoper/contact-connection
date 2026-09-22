using System.Globalization;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.CustomFields;

/// <summary>
/// Formats a CustomFieldValue's typed value as a flow variable string — shared by both engines'
/// "Get Call Record Value" node handlers (CRM's GetCustomFieldNodeHandler and telephony's
/// tf_get_custom_field), since flow variable stores are string-only in both engines.
/// </summary>
public static class CustomFieldValueFormatter
{
    /// <summary>Formats null (no value stored yet for this definition on this call) as "" —
    /// matches the empty-string-for-unresolved convention used elsewhere (e.g. GetSipHeaderNodeHandler).</summary>
    public static string Format(CustomFieldValue? value)
    {
        if (value is null) return string.Empty;

        return value.GetTypedValue() switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dt => dt.ToString("O", CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            var other => other.ToString() ?? string.Empty,
        };
    }
}
