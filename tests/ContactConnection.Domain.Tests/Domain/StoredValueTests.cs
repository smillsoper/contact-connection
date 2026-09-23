using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class StoredValueTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_UnknownScope_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            StoredValue.Create(TenantId, "not_a_real_scope", Guid.Empty, "key", "value", null));
        Assert.Equal("scope", ex.ParamName);
    }

    [Theory]
    [InlineData(StoredValueScope.Tenant)]
    [InlineData(StoredValueScope.Client)]
    [InlineData(StoredValueScope.Campaign)]
    public void Create_KnownScope_Succeeds(string scope)
    {
        var value = StoredValue.Create(TenantId, scope, Guid.NewGuid(), "key", "value", null);
        Assert.Equal(scope, value.Scope);
    }

    [Fact]
    public void IsExpired_NullExpiresAt_NeverExpires()
    {
        var value = StoredValue.Create(TenantId, StoredValueScope.Tenant, Guid.Empty, "key", "value", null);
        Assert.False(value.IsExpired(DateTimeOffset.UtcNow.AddYears(100)));
    }

    [Fact]
    public void IsExpired_FutureExpiresAt_NotYetExpired()
    {
        var value = StoredValue.Create(
            TenantId, StoredValueScope.Tenant, Guid.Empty, "key", "value", DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(value.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_PastExpiresAt_IsExpired()
    {
        var value = StoredValue.Create(
            TenantId, StoredValueScope.Tenant, Guid.Empty, "key", "value", DateTimeOffset.UtcNow.AddHours(-1));
        Assert.True(value.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_ExactlyAtExpiresAt_IsExpired()
    {
        // Boundary is inclusive (<=) — a row whose expiry lands exactly "now" is treated as gone,
        // not kept for one more instant.
        var now = DateTimeOffset.UtcNow;
        var value = StoredValue.Create(TenantId, StoredValueScope.Tenant, Guid.Empty, "key", "value", now);
        Assert.True(value.IsExpired(now));
    }

    [Fact]
    public void UpdateValue_ReplacesValueAndExpiry()
    {
        var value = StoredValue.Create(
            TenantId, StoredValueScope.Campaign, Guid.NewGuid(), "key", "old", DateTimeOffset.UtcNow.AddHours(1));

        var newExpiry = DateTimeOffset.UtcNow.AddDays(1);
        value.UpdateValue("new", newExpiry);

        Assert.Equal("new", value.Value);
        Assert.Equal(newExpiry, value.ExpiresAt);
    }

    [Fact]
    public void UpdateValue_CanClearExpiryToForever()
    {
        var value = StoredValue.Create(
            TenantId, StoredValueScope.Campaign, Guid.NewGuid(), "key", "old", DateTimeOffset.UtcNow.AddHours(1));

        value.UpdateValue("new", null);

        Assert.Null(value.ExpiresAt);
    }
}
