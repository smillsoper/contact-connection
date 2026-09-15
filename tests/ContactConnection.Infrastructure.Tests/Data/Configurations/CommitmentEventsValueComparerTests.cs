using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Data.Configurations;

/// <summary>
/// Regression coverage for the CommitmentEvents JSONB ValueComparer
/// (<see cref="Infrastructure.Data.Configurations.CallRecordConfiguration"/> /
/// <see cref="Infrastructure.Data.Configurations.CallInteractionConfiguration"/>). Without an
/// explicit comparer, EF Core's change tracker snapshots a `List&lt;CommitmentEvent&gt;` by
/// reference, so an in-place `.Add(...)` between load and SaveChanges is never detected and the
/// jsonb column silently never gets written — even though CommitmentEvents IS the call's lock
/// registry. These tests mutate a tracked entity and SaveChanges a second time, then reload from
/// a fresh context to prove the append actually persisted.
/// </summary>
public class CommitmentEventsValueComparerTests
{
    private static CommitmentEvent NewEvent(string name = "order_placed") => new()
    {
        EventName = name,
        LockedFields = ["Cart", "TotalAmount"],
        LockLabel = "Order placed",
        AllowsSupervisorOverride = true,
        OverrideRequiresReason = true,
        OccurredAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task CallRecord_InPlaceAppend_PersistsAcrossSecondSaveChanges()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(dbName).Options;

        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CallSource.Inbound, "+15551230000");

        await using (var db = new TenantDbContext(options))
        {
            db.CallRecords.Add(record);
            await db.SaveChangesAsync();

            // Mutates the same tracked List<CommitmentEvent> in place, then saves again --
            // exactly the pattern that silently no-ops without a ValueComparer.
            record.AddCommitmentEvent(NewEvent());
            await db.SaveChangesAsync();
        }

        await using var reload = new TenantDbContext(options);
        var reloaded = await reload.CallRecords.SingleAsync(r => r.Id == record.Id);

        Assert.Single(reloaded.CommitmentEvents);
        Assert.Equal("order_placed", reloaded.CommitmentEvents[0].EventName);
    }

    [Fact]
    public async Task CallRecord_MultipleAppendsAcrossSeparateSaves_AllPersist()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(dbName).Options;

        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CallSource.Inbound, "+15551230000");

        await using (var db = new TenantDbContext(options))
        {
            db.CallRecords.Add(record);
            await db.SaveChangesAsync();

            record.AddCommitmentEvent(NewEvent("order_placed"));
            await db.SaveChangesAsync();

            record.AddCommitmentEvent(NewEvent("payment_captured"));
            await db.SaveChangesAsync();
        }

        await using var reload = new TenantDbContext(options);
        var reloaded = await reload.CallRecords.SingleAsync(r => r.Id == record.Id);

        Assert.Equal(["order_placed", "payment_captured"], reloaded.CommitmentEvents.Select(e => e.EventName));
    }

    [Fact]
    public async Task CallInteraction_InPlaceAppend_PersistsAcrossSecondSaveChanges()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(dbName).Options;

        var interaction = CallInteraction.Create(Guid.NewGuid(), 1, InteractionType.OrderSale);

        await using (var db = new TenantDbContext(options))
        {
            db.CallInteractions.Add(interaction);
            await db.SaveChangesAsync();

            interaction.AddCommitmentEvent(NewEvent());
            await db.SaveChangesAsync();
        }

        await using var reload = new TenantDbContext(options);
        var reloaded = await reload.CallInteractions.SingleAsync(i => i.Id == interaction.Id);

        Assert.Single(reloaded.CommitmentEvents);
        Assert.Equal("order_placed", reloaded.CommitmentEvents[0].EventName);
    }
}
