namespace ContactConnection.Domain.Entities;

public class BlockListEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string PhoneNumber { get; private set; } = string.Empty;
    public string MatchType { get; private set; } = BlockListMatchType.Exact;
    public string? Reason { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow;

    private BlockListEntry() { }

    public static BlockListEntry Create(
        Guid tenantId,
        string phoneNumber,
        string matchType,
        string? reason,
        DateTimeOffset? expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new BlockListEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PhoneNumber = phoneNumber,
            MatchType = matchType,
            Reason = reason,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string? reason, DateTimeOffset? expiresAt)
    {
        Reason = reason;
        ExpiresAt = expiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Remove()
    {
        ExpiresAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns true if the given phone number matches this entry.
    /// Exact: full string equality. Prefix: number starts with the stored prefix.
    /// </summary>
    public bool Matches(string phoneNumber) => MatchType switch
    {
        BlockListMatchType.Prefix => phoneNumber.StartsWith(PhoneNumber, StringComparison.Ordinal),
        _ => string.Equals(PhoneNumber, phoneNumber, StringComparison.Ordinal)
    };
}

public static class BlockListMatchType
{
    public const string Exact = "exact";
    public const string Prefix = "prefix";
}
