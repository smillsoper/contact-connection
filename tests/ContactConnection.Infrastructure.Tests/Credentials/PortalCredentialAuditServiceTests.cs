using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Credentials;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>Portal-scoped counterpart to TenantCredentialAuditServiceTests — same contract,
/// backed directly by ContactConnectionDbContext (public schema).</summary>
public class PortalCredentialAuditServiceTests
{
    private static PortalCredentialAuditService CreateService()
    {
        var options = new DbContextOptionsBuilder<ContactConnectionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ContactConnectionDbContext(options);
        return new PortalCredentialAuditService(db);
    }

    [Fact]
    public async Task RecordAsync_ThenListAsync_ReturnsEntry()
    {
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("google-places-api-key", CredentialAuditAction.Set, actorId, "Platform Admin");

        var entry = Assert.Single(await service.ListAsync("google-places-api-key"));
        Assert.Equal(CredentialAuditAction.Set, entry.Action);
        Assert.Equal("Platform Admin", entry.ActorName);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("k", CredentialAuditAction.Set, actorId, "Alice");
        await service.RecordAsync("k", CredentialAuditAction.Delete, actorId, "Bob");

        var list = await service.ListAsync("k");
        Assert.Equal(2, list.Count);
        Assert.Equal(CredentialAuditAction.Delete, list[0].Action);
    }

    [Fact]
    public async Task ListAsync_ScopedByKeyName_NeverLeaksAcrossKeys()
    {
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("key-a", CredentialAuditAction.Set, actorId, "Alice");
        await service.RecordAsync("key-b", CredentialAuditAction.Set, actorId, "Alice");

        Assert.Single(await service.ListAsync("key-a"));
    }
}
