using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Versioning;

/// <summary>Covers TenantVersionHistoryService's snapshot/list/get semantics against a real (in-
/// memory) TenantDbContext — one active version per entity, newest-first listing, revert-as-new-
/// version. See API_HARDENING_CHECKLIST.md Tier 2.</summary>
public class TenantVersionHistoryServiceTests
{
    private static TenantVersionHistoryService CreateService()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TenantDbContext(options);

        var tenant = Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");
        var tenantContext = new TenantContext { Current = tenant };

        var factoryMock = new Mock<ITenantDbContextFactory>();
        factoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        var scopedFactory = new ScopedTenantDbContextFactory(tenantContext, factoryMock.Object);
        return new TenantVersionHistoryService(scopedFactory);
    }

    [Fact]
    public async Task SnapshotAsync_FirstCall_CreatesVersion1_Active()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var version = await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"a\":1}", actorId, "Alice", "Created");

        Assert.Equal(1, version);
        var list = await service.ListVersionsAsync(VersionedEntityType.Flow, entityId);
        var only = Assert.Single(list);
        Assert.True(only.IsActive);
        Assert.Equal(1, only.VersionNumber);
        Assert.Equal("Alice", only.CreatedByName);
        Assert.Equal("Created", only.ChangeSummary);
    }

    [Fact]
    public async Task SnapshotAsync_SecondCall_DeactivatesPrevious_ListedNewestFirst()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"a\":1}", actorId, "Alice", "Created");
        var v2 = await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"a\":2}", actorId, "Bob", "Updated");

        Assert.Equal(2, v2);
        var list = await service.ListVersionsAsync(VersionedEntityType.Flow, entityId);
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].VersionNumber); // newest first
        Assert.True(list[0].IsActive);
        Assert.Equal(1, list[1].VersionNumber);
        Assert.False(list[1].IsActive); // exactly one active version at a time
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsCorrectJsonPerVersion_NullForMissingVersion()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"a\":1}", actorId, "Alice");
        await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"a\":2}", actorId, "Bob");

        Assert.Equal("{\"a\":1}", await service.GetSnapshotAsync(VersionedEntityType.Flow, entityId, 1));
        Assert.Equal("{\"a\":2}", await service.GetSnapshotAsync(VersionedEntityType.Flow, entityId, 2));
        Assert.Null(await service.GetSnapshotAsync(VersionedEntityType.Flow, entityId, 99));
    }

    [Fact]
    public async Task RevertScenario_SnapshottingAnOldSnapshotJson_RecordsBrandNewVersion_NeverRewinds()
    {
        // Mirrors what the Flows/API Definition endpoints actually do on revert: fetch an old
        // snapshot's JSON, apply it to the live entity, then snapshot again — never delete or
        // reactivate the old row.
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"name\":\"A\"}", actorId, "Alice", "Created");
        await service.SnapshotAsync(VersionedEntityType.Flow, entityId, "{\"name\":\"B\"}", actorId, "Alice", "Updated");

        var v1Json = await service.GetSnapshotAsync(VersionedEntityType.Flow, entityId, 1);
        var v3 = await service.SnapshotAsync(VersionedEntityType.Flow, entityId, v1Json!, actorId, "Alice", "Reverted to version 1");

        Assert.Equal(3, v3);
        var list = await service.ListVersionsAsync(VersionedEntityType.Flow, entityId);
        Assert.Equal(3, list.Count); // nothing discarded — history only grows
        Assert.Equal("Reverted to version 1", list[0].ChangeSummary);
        Assert.True(list[0].IsActive);
        Assert.False(list.Single(v => v.VersionNumber == 2).IsActive);
    }

    [Fact]
    public async Task ListVersionsAsync_ScopedByEntityTypeAndId_NeverLeaksAcrossEntities()
    {
        var service = CreateService();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.Flow, entityA, "{}", actorId, "Alice");
        await service.SnapshotAsync(VersionedEntityType.TenantApiDefinition, entityA, "{}", actorId, "Alice"); // same id, different type
        await service.SnapshotAsync(VersionedEntityType.Flow, entityB, "{}", actorId, "Alice"); // different id, same type

        var list = await service.ListVersionsAsync(VersionedEntityType.Flow, entityA);
        Assert.Single(list);
    }
}
