using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Repositories;

/// <summary>
/// <see cref="CallRecordRepository.FindWithSensitiveDataOldestFirstAsync"/> — the query the
/// Worker's SensitiveDataRetentionService pulls each batch from. Only records still holding a
/// PCI <c>SensitiveData</c> blob come back; already-wiped or never-captured ones don't.
/// </summary>
public class CallRecordRepositorySensitiveDataRetentionTests
{
    private static (CallRecordRepository repo, TenantDbContext db) NewRepo()
    {
        var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var tenantContext = new TenantContext { Current = Tenant.Create("Test", "test-tenant", "America/Chicago") };
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        return (new CallRecordRepository(new ScopedTenantDbContextFactory(tenantContext, factory.Object)), db);
    }

    private static CallRecord NewRecord() =>
        CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CallSource.Inbound, "+15551230000");

    [Fact]
    public async Task ReturnsOnly_RecordsWithSensitiveData()
    {
        var (repo, db) = NewRepo();

        var withData    = NewRecord();
        withData.StoreSensitiveData("ciphertext");
        var noData       = NewRecord();
        var wipedAlready = NewRecord();
        wipedAlready.StoreSensitiveData("ciphertext");
        wipedAlready.WipeSensitiveData("retention_expired");   // SensitiveData null again

        db.CallRecords.AddRange(withData, noData, wipedAlready);
        await db.SaveChangesAsync();

        var ids = await repo.FindWithSensitiveDataOldestFirstAsync(50);

        Assert.Equal(new[] { withData.Id }, ids);
    }

    [Fact]
    public async Task OrdersOldestStoredFirst()
    {
        var (repo, db) = NewRepo();

        var newer = NewRecord();
        newer.StoreSensitiveData("newer");
        var older = NewRecord();
        older.StoreSensitiveData("older");

        db.CallRecords.AddRange(newer, older);
        await db.SaveChangesAsync();

        // Backdate the "older" row's stored_at directly — StoreSensitiveData always stamps "now".
        db.Entry(older).Property("SensitiveDataStoredAt").CurrentValue =
            DateTimeOffset.UtcNow.AddHours(-2);
        await db.SaveChangesAsync();

        var ids = await repo.FindWithSensitiveDataOldestFirstAsync(50);

        Assert.Equal(new[] { older.Id, newer.Id }, ids);
    }

    [Fact]
    public async Task RespectsLimit()
    {
        var (repo, db) = NewRepo();
        for (var i = 0; i < 5; i++)
        {
            var r = NewRecord();
            r.StoreSensitiveData("x");
            db.CallRecords.Add(r);
        }
        await db.SaveChangesAsync();

        var ids = await repo.FindWithSensitiveDataOldestFirstAsync(3);

        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public async Task Empty_WhenNothingCaptured()
    {
        var (repo, db) = NewRepo();
        db.CallRecords.Add(NewRecord());
        await db.SaveChangesAsync();

        Assert.Empty(await repo.FindWithSensitiveDataOldestFirstAsync(50));
    }
}
