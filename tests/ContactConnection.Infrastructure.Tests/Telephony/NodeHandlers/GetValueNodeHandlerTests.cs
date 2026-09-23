using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class GetValueNodeHandlerTests
{
    private static TelephonyFlowContext Ctx(Guid tenantId) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = tenantId, CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string scope, string keyName, string? variableName) => new()
    {
        ["type"] = "tf_get_value",
        ["scope"] = scope,
        ["keyName"] = keyName,
        ["variableName"] = variableName,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    private static Tenant NewTenant() => Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");

    [Fact]
    public async Task ResolvesKeyTemplate_ReturnsValueIntoSessionVar()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var storedValues = new Mock<IStoredValueService>();
        storedValues.Setup(s => s.GetAsync(ctx.CallRecordId, "campaign", "+15551234567_orderId", It.IsAny<CancellationToken>()))
            .ReturnsAsync("12345");
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new GetValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node("campaign", "{{caller.ani}}_orderId", "lastOrderId"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("12345", ctx.Vars["lastOrderId"]);
    }

    [Fact]
    public async Task PrimesTenantContext_WhenUnset()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var tenant = NewTenant();
        var storedValues = new Mock<IStoredValueService>();
        storedValues.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var tenantContext = new TenantContext();
        var handler = new GetValueNodeHandler(storedValues.Object, tenantRepo.Object, tenantContext);

        await handler.ExecuteAsync(Node("tenant", "key", "myVar"), ctx);

        Assert.Same(tenant, tenantContext.Current);
    }

    [Fact]
    public async Task NoValueStored_ResolvesToEmptyString_NotAFailure()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var storedValues = new Mock<IStoredValueService>();
        storedValues.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new GetValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node("tenant", "key", "myVar"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("", ctx.Vars["myVar"]);
    }

    [Fact]
    public async Task NoVariableName_DoesNotCallService_NoTenantPriming()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var storedValues = new Mock<IStoredValueService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new GetValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        await handler.ExecuteAsync(Node("tenant", "key", null), ctx);

        storedValues.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        tenantRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyKeyAfterResolve_DoesNotCallService_StillWritesEmptyString()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var storedValues = new Mock<IStoredValueService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new GetValueNodeHandler(storedValues.Object, tenantRepo.Object, new TenantContext());

        await handler.ExecuteAsync(Node("tenant", "{{flow.never_set}}", "myVar"), ctx);

        Assert.Equal("", ctx.Vars["myVar"]);
        storedValues.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        tenantRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
