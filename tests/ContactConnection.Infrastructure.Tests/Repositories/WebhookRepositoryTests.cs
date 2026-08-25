using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Repositories;

/// <summary>Covers WebhookEndpointRepository/WebhookEventRepository against a real (in-memory)
/// TenantDbContext — token/tenant-api-endpoint lookups and the ExistsAsync dedup check that
/// backs WebhookReceiveHandler's duplicate-delivery short-circuit. See
/// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".</summary>
public class WebhookRepositoryTests
{
    private static ScopedTenantDbContextFactory CreateScopedFactory(out TenantDbContext db)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new TenantDbContext(options);

        var tenant = Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");
        var tenantContext = new TenantContext { Current = tenant };

        var factoryMock = new Mock<ITenantDbContextFactory>();
        factoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        return new ScopedTenantDbContextFactory(tenantContext, factoryMock.Object);
    }

    [Fact]
    public async Task WebhookEndpoint_AddThenGetByToken_RoundTrips()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEndpointRepository(factory);

        var endpoint = WebhookEndpoint.Create(Guid.NewGuid());
        await repo.AddAsync(endpoint);
        await repo.SaveChangesAsync();

        var found = await repo.GetByTokenAsync(endpoint.Token);
        Assert.NotNull(found);
        Assert.Equal(endpoint.Id, found!.Id);
    }

    [Fact]
    public async Task WebhookEndpoint_GetByTenantApiEndpointId_FindsTheOneAttachedRow()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEndpointRepository(factory);
        var apiEndpointId = Guid.NewGuid();

        await repo.AddAsync(WebhookEndpoint.Create(Guid.NewGuid())); // unrelated row
        var attached = WebhookEndpoint.Create(apiEndpointId);
        await repo.AddAsync(attached);
        await repo.SaveChangesAsync();

        var found = await repo.GetByTenantApiEndpointIdAsync(apiEndpointId);
        Assert.NotNull(found);
        Assert.Equal(attached.Id, found!.Id);
    }

    [Fact]
    public async Task WebhookEndpoint_GetByToken_UnknownToken_ReturnsNull()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEndpointRepository(factory);

        Assert.Null(await repo.GetByTokenAsync("no-such-token"));
    }

    [Fact]
    public async Task WebhookEndpoint_RegenerateToken_OldTokenNoLongerResolves()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEndpointRepository(factory);

        var endpoint = WebhookEndpoint.Create(Guid.NewGuid());
        var oldToken = endpoint.Token;
        await repo.AddAsync(endpoint);
        await repo.SaveChangesAsync();

        endpoint.RegenerateToken();
        await repo.SaveChangesAsync();

        Assert.Null(await repo.GetByTokenAsync(oldToken));
        Assert.NotNull(await repo.GetByTokenAsync(endpoint.Token));
    }

    [Fact]
    public async Task WebhookEndpoint_Delete_RemovesRow()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEndpointRepository(factory);

        var endpoint = WebhookEndpoint.Create(Guid.NewGuid());
        await repo.AddAsync(endpoint);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(endpoint);
        await repo.SaveChangesAsync();

        Assert.Null(await repo.GetByIdAsync(endpoint.Id));
    }

    [Fact]
    public async Task WebhookEvent_ExistsAsync_TrueOnlyForMatchingEndpointAndHash_FalseOtherwise()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEventRepository(factory);
        var endpointId = Guid.NewGuid();
        var otherEndpointId = Guid.NewGuid();

        var evt = WebhookEvent.Create(endpointId, "the-body", "application/json", signatureValid: true);
        await repo.AddAsync(evt);
        await repo.SaveChangesAsync();

        Assert.True(await repo.ExistsAsync(endpointId, evt.BodyHash));
        // Same hash, different endpoint — dedup is scoped per webhook endpoint, not global.
        Assert.False(await repo.ExistsAsync(otherEndpointId, evt.BodyHash));
        // Same endpoint, different body — different hash.
        Assert.False(await repo.ExistsAsync(endpointId, WebhookEvent.ComputeBodyHash("a-different-body")));
    }

    [Fact]
    public async Task WebhookEvent_ListByEndpoint_NewestFirst_ScopedToEndpoint_RespectsTake()
    {
        var factory = CreateScopedFactory(out _);
        var repo = new WebhookEventRepository(factory);
        var endpointId = Guid.NewGuid();

        var e1 = WebhookEvent.Create(endpointId, "body-1", null, true);
        await repo.AddAsync(e1); await repo.SaveChangesAsync();
        var e2 = WebhookEvent.Create(endpointId, "body-2", null, true);
        await repo.AddAsync(e2); await repo.SaveChangesAsync();
        var e3 = WebhookEvent.Create(endpointId, "body-3", null, true);
        await repo.AddAsync(e3); await repo.SaveChangesAsync();
        // Unrelated endpoint's event must never leak into another endpoint's list.
        await repo.AddAsync(WebhookEvent.Create(Guid.NewGuid(), "other-endpoint-body", null, true));
        await repo.SaveChangesAsync();

        var list = await repo.ListByEndpointAsync(endpointId, take: 2);

        Assert.Equal(2, list.Count);
        Assert.All(list, e => Assert.Equal(endpointId, e.WebhookEndpointId));
        Assert.True(list[0].ReceivedAt >= list[1].ReceivedAt);
    }
}
