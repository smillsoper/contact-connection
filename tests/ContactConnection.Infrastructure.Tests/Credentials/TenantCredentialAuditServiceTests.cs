using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Credentials;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>Covers TenantCredentialAuditService — an append-only log of Set/Delete actions
/// against a credential key, never the secret value itself. See
/// API_HARDENING_CHECKLIST.md Tier 1 (credential audit trail, Session 86).</summary>
public class TenantCredentialAuditServiceTests
{
    private static TenantCredentialAuditService CreateService()
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
        return new TenantCredentialAuditService(scopedFactory);
    }

    [Fact]
    public async Task RecordAsync_ThenListAsync_ReturnsEntry_WithoutTheSecretValue()
    {
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("usps_api_key", CredentialAuditAction.Set, actorId, "Alice");

        var entry = Assert.Single(await service.ListAsync("usps_api_key"));
        Assert.Equal("usps_api_key", entry.KeyName);
        Assert.Equal(CredentialAuditAction.Set, entry.Action);
        Assert.Equal(actorId, entry.ActorId);
        Assert.Equal("Alice", entry.ActorName);
        // CredentialAuditEntrySummary has no value/secret field at all — nothing to assert absent,
        // which is itself the point: it's structurally impossible to leak the secret through here.
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("k", CredentialAuditAction.Set, actorId, "Alice");
        await service.RecordAsync("k", CredentialAuditAction.Set, actorId, "Alice"); // rotated
        await service.RecordAsync("k", CredentialAuditAction.Delete, actorId, "Bob");

        var list = await service.ListAsync("k");
        Assert.Equal(3, list.Count);
        Assert.Equal(CredentialAuditAction.Delete, list[0].Action);
        Assert.Equal("Bob", list[0].ActorName);
    }

    [Fact]
    public async Task ListAsync_ScopedByKeyName_NeverLeaksAcrossKeys()
    {
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("key-a", CredentialAuditAction.Set, actorId, "Alice");
        await service.RecordAsync("key-b", CredentialAuditAction.Set, actorId, "Alice");

        var list = await service.ListAsync("key-a");
        Assert.Single(list);
        Assert.Equal("key-a", list[0].KeyName);
    }

    [Fact]
    public async Task DeletingACredential_DoesNotErase_ItsAuditTrail()
    {
        // The audit log is independent of the credential's current existence in Key Vault.
        var service = CreateService();
        var actorId = Guid.NewGuid();

        await service.RecordAsync("k", CredentialAuditAction.Set, actorId, "Alice");
        await service.RecordAsync("k", CredentialAuditAction.Delete, actorId, "Alice");

        Assert.Equal(2, (await service.ListAsync("k")).Count);
    }

    [Fact]
    public async Task ListAsync_UnknownKey_ReturnsEmpty()
    {
        var service = CreateService();
        Assert.Empty(await service.ListAsync("never-touched"));
    }
}
