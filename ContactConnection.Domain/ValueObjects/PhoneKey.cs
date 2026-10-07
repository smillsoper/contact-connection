namespace ContactConnection.Domain.ValueObjects;

/// <summary>
/// A phone number reduced to what identifies it (S181): the last 10 digits, ignoring "+1" and formatting — so
/// "+15416413898", "5416413898" and "(541) 641-3898" match. Used for caller history (ANI) and widget DNIS filters.
/// </summary>
public static class PhoneKey
{
    /// <summary>Null when the number is too short to match on (anonymous, extensions).</summary>
    public static string? Of(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return null;
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    /// <summary>A set of keys from a list of numbers; null when the list is empty (= no filter).</summary>
    public static HashSet<string>? Set(IEnumerable<string>? numbers)
    {
        var keys = (numbers ?? []).Select(Of).OfType<string>().ToHashSet();
        return keys.Count == 0 ? null : keys;
    }

    /// <summary>True when there's no filter, or the number is one of the filter's.</summary>
    public static bool Matches(IReadOnlySet<string>? filter, string? phone) =>
        filter is null || (Of(phone) is { } key && filter.Contains(key));
}
