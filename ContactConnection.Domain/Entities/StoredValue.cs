namespace ContactConnection.Domain.Entities;

/// <summary>
/// A generic, free-form key/value pair scoped to a tenant, client, or campaign, with optional
/// expiration. Deliberately separate from CustomFieldValue (typed, definition-driven, always tied
/// to a call record — feeds reporting) and from the shared call-variable store (call-record-scoped,
/// Redis-only, no retention beyond the live call) — this is cross-call cache/scratch data with no
/// pre-defined schema. See ARCHITECTURE.md's Custom Fields section for the sibling it's
/// deliberately distinct from.
/// </summary>
public class StoredValue
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Scope { get; private set; } = "";       // StoredValueScope.*
    public Guid ScopeId { get; private set; }              // Guid.Empty for tenant scope
    public string KeyName { get; private set; } = "";
    public string Value { get; private set; } = "";
    public DateTimeOffset? ExpiresAt { get; private set; } // null = forever
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private StoredValue() { }

    public static StoredValue Create(
        Guid tenantId, string scope, Guid scopeId, string keyName, string value, DateTimeOffset? expiresAt)
    {
        if (!StoredValueScope.All.Contains(scope))
            throw new ArgumentException($"Unknown scope: {scope}", nameof(scope));

        var now = DateTimeOffset.UtcNow;
        return new StoredValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Scope = scope,
            ScopeId = scopeId,
            KeyName = keyName,
            Value = value,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateValue(string value, DateTimeOffset? expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } exp && exp <= now;
}

public static class StoredValueScope
{
    public const string Tenant = "tenant";
    public const string Client = "client";
    public const string Campaign = "campaign";

    public static readonly HashSet<string> All = [Tenant, Client, Campaign];
}
