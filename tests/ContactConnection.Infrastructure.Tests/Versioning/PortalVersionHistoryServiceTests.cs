using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Versioning;

/// <summary>Portal-scoped counterpart to TenantVersionHistoryServiceTests — same contract,
/// backed directly by ContactConnectionDbContext (public schema) rather than going through the
/// tenant-scoped factory indirection.</summary>
public class PortalVersionHistoryServiceTests
{
    private static PortalVersionHistoryService CreateService()
    {
        var options = new DbContextOptionsBuilder<ContactConnectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ContactConnectionDbContext(options);
        return new PortalVersionHistoryService(db);
    }

    [Fact]
    public async Task SnapshotAsync_FirstCall_CreatesVersion1_Active()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var version = await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, "{\"name\":\"A\"}", actorId, "Alice", "Created");

        Assert.Equal(1, version);
        var only = Assert.Single(await service.ListVersionsAsync(VersionedEntityType.PortalApiDefinition, entityId));
        Assert.True(only.IsActive);
        Assert.Equal("Created", only.ChangeSummary);
    }

    [Fact]
    public async Task SnapshotAsync_SecondCall_DeactivatesPrevious_ListedNewestFirst()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, "{\"name\":\"A\"}", actorId, "Alice", "Created");
        await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, "{\"name\":\"B\"}", actorId, "Alice", "Updated");

        var list = await service.ListVersionsAsync(VersionedEntityType.PortalApiDefinition, entityId);
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].VersionNumber);
        Assert.True(list[0].IsActive);
        Assert.False(list[1].IsActive);
    }

    [Fact]
    public async Task RevertScenario_RecordsBrandNewVersion_NeverRewinds()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, "{\"name\":\"A\"}", actorId, "Alice", "Created");
        await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, "{\"name\":\"B\"}", actorId, "Alice", "Updated");

        var v1Json = await service.GetSnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, 1);
        var v3 = await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, v1Json!, actorId, "Alice", "Reverted to version 1");

        Assert.Equal(3, v3);
        var list = await service.ListVersionsAsync(VersionedEntityType.PortalApiDefinition, entityId);
        Assert.Equal(3, list.Count); // nothing discarded — history only grows
        Assert.Equal("Reverted to version 1", list[0].ChangeSummary);
        Assert.True(list[0].IsActive);
        // The new active version genuinely carries v1's content forward, not just its label.
        Assert.Equal(v1Json, await service.GetSnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, 3));
    }

    [Fact]
    public async Task GetSnapshotAsync_UnknownVersion_ReturnsNull()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();

        Assert.Null(await service.GetSnapshotAsync(VersionedEntityType.PortalApiDefinition, entityId, 1));
    }

    [Fact]
    public async Task ListVersionsAsync_ScopedByEntityTypeAndId_NeverLeaksAcrossEntities()
    {
        var service = CreateService();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityA, "{}", actorId, "Alice");
        await service.SnapshotAsync(VersionedEntityType.PortalApiEndpoint, entityA, "{}", actorId, "Alice");
        await service.SnapshotAsync(VersionedEntityType.PortalApiDefinition, entityB, "{}", actorId, "Alice");

        var list = await service.ListVersionsAsync(VersionedEntityType.PortalApiDefinition, entityA);
        Assert.Single(list);
    }
}
