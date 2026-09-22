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
/// tf_set_custom_field — telephony twin of the CRM engine's SetCustomFieldNodeHandler. Runs from a
/// background service (EslBackgroundService) with no ambient HTTP-request-scoped TenantContext, so
/// (like ScriptPopNodeHandler) it must prime TenantContext.Current itself before calling
/// ICustomFieldService, whose repositories resolve their tenant DB context off it.
/// </summary>
public class SetCustomFieldNodeHandlerTests
{
    private static TelephonyFlowContext Ctx(Guid tenantId) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = tenantId, CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string? definitionId, string value) => new()
    {
        ["type"] = "tf_set_custom_field",
        ["definitionId"] = definitionId,
        ["value"] = value,
        ["transitions"] = new JsonObject
        {
            ["success"] = "n_success", ["invalid_value"] = "n_invalid", ["error"] = "n_error",
        },
    };

    private static Tenant NewTenant() => Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");

    [Fact]
    public async Task SuccessfulSet_TakesSuccessTransition_ResolvesValueTemplate()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        ctx.Vars["entered_phone"] = "5416704541";
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node(definitionId.ToString(), "{{flow.entered_phone}}"), ctx);

        Assert.Equal("n_success", result.NextNodeId);
        Assert.Equal("success", result.TransitionTaken);
        customFields.Verify(s => s.SetValueAsync(ctx.CallRecordId, definitionId, "5416704541", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrimesTenantContext_WhenUnset()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var tenant = NewTenant();
        var customFields = new Mock<ICustomFieldService>();
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var tenantContext = new TenantContext();
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, tenantContext);

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString(), "x"), ctx);

        Assert.Same(tenant, tenantContext.Current);
        tenantRepo.Verify(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotOverwrite_AlreadySetTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var existingTenant = NewTenant();
        var tenantContext = new TenantContext { Current = existingTenant };
        var customFields = new Mock<ICustomFieldService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, tenantContext);

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString(), "x"), ctx);

        Assert.Same(existingTenant, tenantContext.Current);
        tenantRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FormatException_TakesInvalidValueTransition()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.SetValueAsync(ctx.CallRecordId, definitionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("not a number"));
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node(definitionId.ToString(), "not-a-number"), ctx);

        Assert.Equal("n_invalid", result.NextNodeId);
    }

    [Fact]
    public async Task InvalidOperationException_TakesErrorTransition()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var definitionId = Guid.NewGuid();
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.SetValueAsync(ctx.CallRecordId, definitionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("definition not found"));
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node(definitionId.ToString(), "x"), ctx);

        Assert.Equal("n_error", result.NextNodeId);
    }

    [Fact]
    public async Task NoDefinitionId_TakesErrorTransition_NoServiceCall_NoTenantPriming()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var customFields = new Mock<ICustomFieldService>();
        var tenantRepo = new Mock<ITenantRepository>();
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(Node(null, "x"), ctx);

        Assert.Equal("n_error", result.NextNodeId);
        customFields.Verify(s => s.SetValueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        tenantRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingTransition_FallsBackToDefault()
    {
        var tenantId = Guid.NewGuid();
        var ctx = Ctx(tenantId);
        var node = Node(Guid.NewGuid().ToString(), "x");
        ((JsonObject)node["transitions"]!).Remove("error");
        ((JsonObject)node["transitions"]!)["default"] = "n_fallback";
        var customFields = new Mock<ICustomFieldService>();
        customFields.Setup(s => s.SetValueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(NewTenant());
        var handler = new SetCustomFieldNodeHandler(customFields.Object, tenantRepo.Object, new TenantContext());

        var result = await handler.ExecuteAsync(node, ctx);

        Assert.Equal("n_fallback", result.NextNodeId);
    }
}
