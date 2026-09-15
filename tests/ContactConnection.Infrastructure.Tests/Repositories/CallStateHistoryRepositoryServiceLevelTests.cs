using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Repositories;

/// <summary>
/// <see cref="CallStateHistoryRepository.GetServiceLevelStatsAsync"/> -- the aggregation behind
/// the Service Level dashboard widget. Counts rows with a stamped MetServiceLevel (only the
/// Active/answered transition carries one; everything else is null and must be ignored),
/// scoped by campaign and by a since-cutoff.
/// </summary>
public class CallStateHistoryRepositoryServiceLevelTests
{
    private static (CallStateHistoryRepository repo, TenantDbContext db) NewRepo()
    {
        var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        return (new CallStateHistoryRepository(factory.Object), db);
    }

    private static CallStateHistoryEntry Entry(
        Guid campaignId, bool? metServiceLevel, DateTimeOffset enteredAt, string state = CallHistoryState.Active)
    {
        var entry = CallStateHistoryEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), sequence: 1, state: state, campaignId: campaignId,
            agentId: Guid.NewGuid(), detail: null, abandonType: null, abandonLength: null,
            metServiceLevel: metServiceLevel);
        return SetEnteredAt(entry, enteredAt);
    }

    // EnteredAt is stamped to DateTimeOffset.UtcNow inside Create() with a private setter --
    // reflection is the only way to backdate a row for the sinceUtc-boundary tests below.
    private static CallStateHistoryEntry SetEnteredAt(CallStateHistoryEntry entry, DateTimeOffset enteredAt)
    {
        typeof(CallStateHistoryEntry).GetProperty(nameof(CallStateHistoryEntry.EnteredAt))!
            .SetValue(entry, enteredAt);
        return entry;
    }

    [Fact]
    public async Task CountsMetAndMissed_IgnoringNullRows()
    {
        var (repo, db) = NewRepo();
        var campaignId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.CallStateHistory.AddRange(
            Entry(campaignId, true, now),
            Entry(campaignId, true, now),
            Entry(campaignId, false, now),
            Entry(campaignId, null, now),                       // never queued -- must be ignored
            Entry(campaignId, null, now, CallHistoryState.InQueue)); // non-Active row -- ignored
        await db.SaveChangesAsync();

        var stats = await repo.GetServiceLevelStatsAsync("tenant_test", null, now.AddMinutes(-1));

        Assert.Equal(2, stats.Met);
        Assert.Equal(1, stats.Missed);
    }

    [Fact]
    public async Task ScopesToGivenCampaignIds()
    {
        var (repo, db) = NewRepo();
        var inScope = Guid.NewGuid();
        var outOfScope = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.CallStateHistory.AddRange(
            Entry(inScope, true, now),
            Entry(outOfScope, true, now),
            Entry(outOfScope, false, now));
        await db.SaveChangesAsync();

        var stats = await repo.GetServiceLevelStatsAsync("tenant_test", [inScope], now.AddMinutes(-1));

        Assert.Equal(1, stats.Met);
        Assert.Equal(0, stats.Missed);
    }

    [Fact]
    public async Task ExcludesRowsBeforeSinceUtc()
    {
        var (repo, db) = NewRepo();
        var campaignId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.CallStateHistory.AddRange(
            Entry(campaignId, true, now),                    // in window
            Entry(campaignId, true, now.AddHours(-2)));       // before the cutoff
        await db.SaveChangesAsync();

        var stats = await repo.GetServiceLevelStatsAsync("tenant_test", null, now.AddHours(-1));

        Assert.Equal(1, stats.Met);
        Assert.Equal(0, stats.Missed);
    }
}
