namespace ContactConnection.Infrastructure.StoredValues;

/// <summary>
/// Maps the Store Value node's retention dropdown key to a concrete expiry timestamp — shared by
/// both engines' Set node handlers so the mapping only lives in one place.
/// </summary>
public static class RetentionOptions
{
    public static DateTimeOffset? ResolveExpiresAt(string? retentionKey) => retentionKey switch
    {
        "1_hour" => DateTimeOffset.UtcNow.AddHours(1),
        "24_hours" => DateTimeOffset.UtcNow.AddHours(24),
        "1_week" => DateTimeOffset.UtcNow.AddDays(7),
        "1_month" => DateTimeOffset.UtcNow.AddMonths(1),
        _ => null, // "forever" or unrecognized
    };
}
