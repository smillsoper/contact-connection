using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Credentials;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>Portal-scoped counterpart to CachedTenantCredentialStoreTests — same caching
/// contract, but the cache key is just the key name (portal credentials have no tenant
/// scoping).</summary>
[Collection("Redis")]
public class CachedPortalCredentialStoreTests(RedisFixture fixture)
{
    private static (CachedPortalCredentialStore Store, Mock<IPortalCredentialStore> Inner) Create(RedisFixture fixture)
    {
        var inner = new Mock<IPortalCredentialStore>();
        var store = new CachedPortalCredentialStore(inner.Object, fixture.Connection);
        return (store, inner);
    }

    [Fact]
    public async Task GetAsync_CacheMiss_FallsThroughToInner_AndCachesResult()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture);
        inner.Setup(i => i.GetAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync("portal-secret");

        var first = await store.GetAsync(key);
        var second = await store.GetAsync(key);

        Assert.Equal("portal-secret", first);
        Assert.Equal("portal-secret", second);
        inner.Verify(i => i.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WritesThroughToInner_AndEvictsCache_SoTheRotatedValueIsServedNext()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture);
        inner.SetupSequence(i => i.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync("old-value")
            .ReturnsAsync("new-value");

        Assert.Equal("old-value", await store.GetAsync(key)); // populates cache

        await store.SetAsync(key, "new-value");
        Assert.Equal("new-value", await store.GetAsync(key));

        inner.Verify(i => i.SetAsync(key, "new-value", It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(i => i.GetAsync(key, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteAsync_CallsInnerDelete_AndEvictsCache()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var (store, inner) = Create(fixture);
        inner.SetupSequence(i => i.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync("still-here")
            .ReturnsAsync((string?)null);

        _ = await store.GetAsync(key);
        await store.DeleteAsync(key);

        Assert.Null(await store.GetAsync(key));
        inner.Verify(i => i.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(i => i.GetAsync(key, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ListAsync_NeverCached_AlwaysCallsInner()
    {
        var (store, inner) = Create(fixture);
        inner.Setup(i => i.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await store.ListAsync();
        await store.ListAsync();

        inner.Verify(i => i.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
