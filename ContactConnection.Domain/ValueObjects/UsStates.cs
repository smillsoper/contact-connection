using System.Diagnostics.CodeAnalysis;

namespace ContactConnection.Domain.ValueObjects;

/// <summary>US state, DC and territory postal codes — for validating state-keyed configuration
/// such as per-state tax rates. Kept in sync with ContactConnection.Web/src/constants/usStates.ts.</summary>
public static class UsStates
{
    private static readonly HashSet<string> _codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY","LA",
        "ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND","OH","OK",
        "OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY",
        "DC","PR","GU","VI","AS","MP",
    };

    public static bool IsValid([NotNullWhen(true)] string? code) => code is not null && _codes.Contains(code);
}
