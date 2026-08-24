using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Tests.Credentials;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>Covers RedisOutboundRateLimiter — the fixed-window counter behind
/// API_HARDENING_CHECKLIST.md Tier 2's outbound rate limiting. Uses a real local Redis, same as
/// the other Redis-backed components (see RedisFixture's rationale). windowSeconds is kept tiny
/// in the rollover test so it doesn't need to wait out a real minute.</summary>
[Collection("Redis")]
public class RedisOutboundRateLimiterTests(RedisFixture fixture)
{
    private RedisOutboundRateLimiter CreateLimiter(int windowSeconds = 60) => new(fixture.Connection, windowSeconds);

    [Fact]
    public async Task TryAcquireAsync_NullLimit_AlwaysAllowed()
    {
        var limiter = CreateLimiter();
        var definitionId = Guid.NewGuid();

        for (var i = 0; i < 20; i++)
            Assert.True((await limiter.TryAcquireAsync(definitionId, null)).Allowed);
    }

    [Fact]
    public async Task TryAcquireAsync_ZeroOrNegativeLimit_AlwaysAllowed()
    {
        var limiter = CreateLimiter();
        var definitionId = Guid.NewGuid();

        Assert.True((await limiter.TryAcquireAsync(definitionId, 0)).Allowed);
        Assert.True((await limiter.TryAcquireAsync(definitionId, -5)).Allowed);
    }

    [Fact]
    public async Task TryAcquireAsync_UnderLimit_Allowed()
    {
        var limiter = CreateLimiter();
        var definitionId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            Assert.True((await limiter.TryAcquireAsync(definitionId, 5)).Allowed);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedsLimit_DeniedWithRetryAfter()
    {
        var limiter = CreateLimiter();
        var definitionId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            Assert.True((await limiter.TryAcquireAsync(definitionId, 3)).Allowed);

        var denied = await limiter.TryAcquireAsync(definitionId, 3);
        Assert.False(denied.Allowed);
        Assert.True(denied.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task TryAcquireAsync_DistinctDefinitionIds_DoNotShareBudget()
    {
        // The actual multi-tenant scenario this exists for is the inverse (a shared Portal
        // definition ID deliberately sharing one budget across tenants) — this proves the other
        // half: two genuinely different definitions never bleed into each other's count.
        var limiter = CreateLimiter();
        var defA = Guid.NewGuid();
        var defB = Guid.NewGuid();

        Assert.True((await limiter.TryAcquireAsync(defA, 1)).Allowed);
        Assert.False((await limiter.TryAcquireAsync(defA, 1)).Allowed); // A is now exhausted
        Assert.True((await limiter.TryAcquireAsync(defB, 1)).Allowed);  // B is untouched
    }

    [Fact]
    public async Task TryAcquireAsync_SharedDefinitionId_MultipleCallersShareOneBudget()
    {
        // The actual point of keying per definitionId: every tenant using the same shared Portal
        // definition (a platform-default credential) contends for the same window.
        var limiter = CreateLimiter();
        var sharedDefinitionId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => limiter.TryAcquireAsync(sharedDefinitionId, 4)));

        Assert.Equal(4, results.Count(r => r.Allowed));
        Assert.Equal(6, results.Count(r => !r.Allowed));
    }

    [Fact]
    public async Task TryAcquireAsync_WindowRollsOver_BudgetResets()
    {
        var limiter = CreateLimiter(windowSeconds: 1);
        var definitionId = Guid.NewGuid();

        Assert.True((await limiter.TryAcquireAsync(definitionId, 1)).Allowed);
        Assert.False((await limiter.TryAcquireAsync(definitionId, 1)).Allowed);

        await Task.Delay(TimeSpan.FromSeconds(1.2));

        Assert.True((await limiter.TryAcquireAsync(definitionId, 1)).Allowed);
    }
}
