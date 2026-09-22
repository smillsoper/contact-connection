using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class GetCustomFieldNodeHandlerTests
{
    private static TelephonyFlowContext Ctx(Guid tenantId) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = tenantId, CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string? definitionId, string? variableName) => new()
    {
        ["type"] = "tf_get_custom_field",
        ["definitionId"] = definitionId,
        ["variableName"] = variableName,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    private static Tenant NewTenant() => Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");

    private static CustomFieldDefinition Def(Guid tenantId, string dataType = "string") =>
        CustomFieldDefinition.Create(tenantId, "f", "F", dataType);

    [Fact]
    public async Task StringValue_StoresRawStringIntoFlowVar()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var def = Def(tenantId);
        var value = CustomFieldValue.Create(ctx.CallRecordId, def.Id);
        value.SetString("east coast");
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(ctx.CallRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedCustomField(def, value)]);
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new GetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node(def.Id.ToString(), "region"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("east coast", ctx.Vars["region"]);
    }

    [Fact]
    public async Task PrimesTenantContext_WhenUnset()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var tenant = NewTenant();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var tenantContext = new TenantContext();
        var handler = new GetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, tenantContext);

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString(), "region"), ctx);

        Assert.Same(tenant, tenantContext.Current);
    }

    [Fact]
    public async Task NoValueYetStored_ResolvesToEmptyString_NotAFailure()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var def = Def(tenantId);
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.GetFieldsForCallAsync(ctx.CallRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedCustomField(def, null)]);
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new GetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node(def.Id.ToString(), "region"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        Assert.Equal("", ctx.Vars["region"]);
    }

    [Fact]
    public async Task NoVariableName_DoesNotCallService_NoTenantPriming()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var customFields = new Mock<ICustomFieldService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new GetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString(), null), ctx);

        customFields.Verify(s => s.GetFieldsForCallAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        tenantRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MalformedDefinitionGuid_DoesNotCallService()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var customFields = new Mock<ICustomFieldService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new GetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node("not-a-guid", "region"), ctx);

        Assert.Equal("n_next", result.NextNodeId);
        customFields.Verify(s => s.GetFieldsForCallAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
