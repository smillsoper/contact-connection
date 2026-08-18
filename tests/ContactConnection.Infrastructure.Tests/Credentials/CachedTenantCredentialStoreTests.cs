using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Credentials;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>Covers CachedTenantCredentialStore's caching contract — cache-miss falls through to
/// the inner (Key Vault) store and populates the cache, a cache hit never re-touches the inner
/// store, and Set/Delete evict immediately so a rotated or removed credential is never served
/// stale. Only the inner store is mocked; Redis is real (see RedisFixture).</summary>
[Collection("Redis")]
public class CachedTenantCredentialStoreTests(RedisFixture fixture)
{
    private static (CachedTenantCredentialStore Store, Mock<ITenantCredentialStore> Inner) Create(
        RedisFixture fixture, string subdomain)
    {
        var inner = new Mock<ITenantCredentialStore>();
        var tenant = Tenant.Create("Test Tenant", subdomain, "America/Chicago");
        var tenantContext = new TenantContext { Current = tenant };
        var store = new CachedTenantCredentialStore(inner.Object, fixture.Connection, tenantContext);
        return (store, inner);
    }

    [Fact]
    public async Task GetAsync_CacheMiss_FallsThroughToInner_AndCachesResult()
    {
        var subdomain = $"t-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture, subdomain);
        inner.Setup(i => i.GetAsync("api_key", It.IsAny<CancellationToken>())).ReturnsAsync("secret-value");

        var first = await store.GetAsync("api_key");
        var second = await store.GetAsync("api_key");

        Assert.Equal("secret-value", first);
        Assert.Equal("secret-value", second);
        inner.Verify(i => i.GetAsync("api_key", It.IsAny<CancellationToken>()), Times.Once); // second call served from cache
    }

    [Fact]
    public async Task GetAsync_InnerReturnsNull_CachedToo_NeverHitsInnerTwice()
    {
        var subdomain = $"t-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture, subdomain);
        inner.Setup(i => i.GetAsync("missing_key", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Null(await store.GetAsync("missing_key"));
        Assert.Null(await store.GetAsync("missing_key"));
        inner.Verify(i => i.GetAsync("missing_key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WritesThroughToInner_AndEvictsCache_SoTheRotatedValueIsServedNext()
    {
        var subdomain = $"t-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture, subdomain);
        inner.SetupSequence(i => i.GetAsync("rotating_key", It.IsAny<CancellationToken>()))
            .ReturnsAsync("old-value")
            .ReturnsAsync("new-value");

        var before = await store.GetAsync("rotating_key"); // populates cache with "old-value"
        Assert.Equal("old-value", before);

        await store.SetAsync("rotating_key", "new-value"); // must evict the cached "old-value"
        var after = await store.GetAsync("rotating_key");

        Assert.Equal("new-value", after);
        inner.Verify(i => i.SetAsync("rotating_key", "new-value", It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(i => i.GetAsync("rotating_key", It.IsAny<CancellationToken>()), Times.Exactly(2)); // eviction forced a real re-read
    }

    [Fact]
    public async Task DeleteAsync_CallsInnerDelete_AndEvictsCache()
    {
        var subdomain = $"t-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture, subdomain);
        inner.SetupSequence(i => i.GetAsync("doomed_key", It.IsAny<CancellationToken>()))
            .ReturnsAsync("still-here")
            .ReturnsAsync((string?)null);

        _ = await store.GetAsync("doomed_key"); // populates cache
        await store.DeleteAsync("doomed_key");
        var afterDelete = await store.GetAsync("doomed_key");

        Assert.Null(afterDelete);
        inner.Verify(i => i.DeleteAsync("doomed_key", It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(i => i.GetAsync("doomed_key", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetForTenantAsync_ScopedByExplicitSubdomain_CachesIndependentlyOfAmbientTenant()
    {
        var ambientSubdomain = $"ambient-{Guid.NewGuid():N}";
        var explicitSubdomain = $"explicit-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture, ambientSubdomain); // ambient TenantContext never matches explicitSubdomain
        inner.Setup(i => i.GetForTenantAsync(explicitSubdomain, "k", It.IsAny<CancellationToken>()))
            .ReturnsAsync("value-for-explicit-tenant");

        var first = await store.GetForTenantAsync(explicitSubdomain, "k");
        var second = await store.GetForTenantAsync(explicitSubdomain, "k");

        Assert.Equal("value-for-explicit-tenant", first);
        Assert.Equal("value-for-explicit-tenant", second);
        inner.Verify(i => i.GetForTenantAsync(explicitSubdomain, "k", It.IsAny<CancellationToken>()), Times.Once);
        // The ambient-scoped GetAsync must never have been called — this really did use the
        // explicit-subdomain code path, not silently fall back to ambient TenantContext.
        inner.Verify(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_NeverCached_AlwaysCallsInner()
    {
        var subdomain = $"t-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture, subdomain);
        inner.Setup(i => i.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await store.ListAsync();
        await store.ListAsync();

        inner.Verify(i => i.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
