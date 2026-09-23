using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_store_value — telephony twin of the CRM engine's StoreValueNodeHandler. Runs from a
/// background service with no ambient HTTP-request-scoped TenantContext, so — like the Custom
/// Field telephony handlers — it must prime TenantContext.Current itself before calling
/// IStoredValueService, whose repositories resolve their tenant DB context off it.
/// </summary>
public class StoreValueNodeHandlerTests
{
    private static TelephonyFlowContext Ctx(Guid tenantId) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = tenantId, CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string scope, string keyName, string value, string? retention = null) => new()
    {
        ["type"] = "tf_store_value",
        ["scope"] = scope,
        ["keyName"] = keyName,
        ["value"] = value,
        ["retention"] = retention,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    private static Tenant NewTenant() => Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");

    [Fact]
    public async Task ResolvesKeyAndValueTemplates_BeforeCallingService()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        ctx.Vars["order_id"] = "12345";
        var storedValues = new Mock<IStoredValueService>();
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new StoreValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(
            Node("campaign", "{{caller.ani}}_orderId", "{{flow.order_id}}"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        storedValues.Verify(s => s.SetAsync(
            ctx.CallRecordId, "campaign", "+15551234567_orderId", "12345", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrimesTenantContext_WhenUnset()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var tenant = NewTenant();
        var storedValues = new Mock<IStoredValueService>();
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var tenantContext = new TenantContext();
        var handler = new StoreValueNodeHandler(storedValues.Object, tenantRepo.Object, tenantContext);

        await handler.ExecuteAsync(Node("tenant", "key", "value"), ctx);

        Assert.Same(tenant, tenantContext.Current);
    }

    [Fact]
    public async Task WithRetention_PassesAFutureExpiresAt()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var storedValues = new Mock<IStoredValueService>();
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new StoreValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        await handler.ExecuteAsync(Node("tenant", "key", "value", "24_hours"), ctx);

        storedValues.Verify(s => s.SetAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<DateTimeOffset?>(d => d.HasValue && d.Value > DateTimeOffset.UtcNow),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyKeyAfterResolve_SkipsServiceCall_NoTenantPriming()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var storedValues = new Mock<IStoredValueService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new StoreValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node("tenant", "{{flow.never_set}}", "value"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        storedValues.Verify(s => s.SetAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
        tenantRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
