using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.CustomFields;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.CustomFields;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.CustomFields;

public class CustomFieldServiceTests
{
    private readonly Mock<ICustomFieldDefinitionRepository> _definitions = new();
    private readonly Mock<ICustomFieldValueRepository> _values = new();
    private readonly Mock<ICallRecordRepository> _callRecords = new();
    private readonly Mock<ICampaignRepository> _campaigns = new();

    private CustomFieldService NewService() => new(_definitions.Object, _values.Object, _callRecords.Object, _campaigns.Object);

    private static CallRecord NewCallRecord(Guid tenantId, Guid clientId, Guid campaignId)
        => CallRecord.Create(tenantId, clientId, campaignId);

    [Fact]
    public async Task SetValueAsync_DefinitionScopedToDifferentClient_ThrowsInvalidOperation()
    {
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), campaignId);
        // Definition is scoped to a client that does NOT match the call record's own client —
        // the exact shape of the S152 live bug (a value that writes but can never be read back).
        var def = CustomFieldDefinition.Create(tenantId, "selected_main_offer", "Selected Main Offer",
            CustomFieldDataType.Boolean, clientId: Guid.NewGuid());

        _callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService().SetValueAsync(record.Id, def.Id, "true"));

        _values.Verify(v => v.AddAsync(It.IsAny<CustomFieldValue>(), It.IsAny<CancellationToken>()), Times.Never);
        _values.Verify(v => v.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetValueAsync_DefinitionScopedToDifferentCampaign_ThrowsInvalidOperation()
    {
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, clientId, Guid.NewGuid());
        var def = CustomFieldDefinition.Create(tenantId, "field", "Field",
            CustomFieldDataType.String, clientId: clientId, campaignId: Guid.NewGuid());

        _callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService().SetValueAsync(record.Id, def.Id, "x"));
    }

    [Fact]
    public async Task SetValueAsync_InactiveDefinition_ThrowsInvalidOperation()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var def = CustomFieldDefinition.Create(tenantId, "field", "Field", CustomFieldDataType.String);
        def.Deactivate();

        _callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService().SetValueAsync(record.Id, def.Id, "x"));
    }

    [Fact]
    public async Task SetValueAsync_TenantWideDefinition_InScopeForAnyClientOrCampaign()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var def = CustomFieldDefinition.Create(tenantId, "field", "Field", CustomFieldDataType.String);

        _callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);
        _values.Setup(v => v.GetByCallRecordAndDefinitionAsync(record.Id, def.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync((CustomFieldValue?)null);
        _values.Setup(v => v.GetByCallRecordAsync(record.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync([]);

        await NewService().SetValueAsync(record.Id, def.Id, "hello");

        _values.Verify(v => v.AddAsync(It.IsAny<CustomFieldValue>(), It.IsAny<CancellationToken>()), Times.Once);
        _values.Verify(v => v.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetValueAsync_ClientScopedDefinition_MatchingClient_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, clientId, Guid.NewGuid());
        var def = CustomFieldDefinition.Create(tenantId, "field", "Field", CustomFieldDataType.String, clientId: clientId);

        _callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);
        _values.Setup(v => v.GetByCallRecordAndDefinitionAsync(record.Id, def.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync((CustomFieldValue?)null);
        _values.Setup(v => v.GetByCallRecordAsync(record.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync([]);

        await NewService().SetValueAsync(record.Id, def.Id, "hello");

        _values.Verify(v => v.AddAsync(It.IsAny<CustomFieldValue>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── S178: script writes on a transferred interaction (sales → CS) ─────────────────────────────

    private (CallRecord Record, CallInteraction Sales, CallInteraction Cs, Guid CsCampaign) TransferredCall(Guid tenantId)
    {
        var salesCampaign = Guid.NewGuid();
        var csCampaign = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), salesCampaign);
        var sales = record.AddInteraction(InteractionType.CustomerService);
        sales.AssignTo(Guid.NewGuid(), salesCampaign);
        var cs = record.AddInteraction(InteractionType.CustomerService);
        cs.AssignTo(Guid.NewGuid(), csCampaign);
        _callRecords.Setup(r => r.GetByIdWithInteractionsAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _campaigns.Setup(c => c.GetByIdAsync(csCampaign, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Campaign.Create(tenantId, Guid.NewGuid(), "CS", "cs"));
        return (record, sales, cs, csCampaign);
    }

    [Fact]
    public async Task FromScript_TransferredInteraction_WritesToTheInteraction_NotTheRecord()
    {
        var tenantId = Guid.NewGuid();
        var (record, _, cs, _) = TransferredCall(tenantId);
        var def = CustomFieldDefinition.Create(tenantId, "disposition", "Disposition", CustomFieldDataType.String);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await NewService().SetValueFromScriptAsync(record.Id, cs.Id, def.Id, "Customer Service");

        Assert.Contains("\"disposition\":\"Customer Service\"", cs.CustomFields);
        _values.Verify(v => v.AddAsync(It.IsAny<CustomFieldValue>(), It.IsAny<CancellationToken>()), Times.Never);
        _callRecords.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FromScript_FirstInteraction_WritesToTheRecordAsBefore()
    {
        var tenantId = Guid.NewGuid();
        var (record, sales, _, _) = TransferredCall(tenantId);
        var def = CustomFieldDefinition.Create(tenantId, "disposition", "Disposition", CustomFieldDataType.String);
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);
        _values.Setup(v => v.GetByCallRecordAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await NewService().SetValueFromScriptAsync(record.Id, sales.Id, def.Id, "Transferred to Customer Service");

        _values.Verify(v => v.AddAsync(It.IsAny<CustomFieldValue>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(sales.CustomFields);
    }

    [Fact]
    public async Task FromScript_TransferredInteraction_FieldScopedToAnotherCampaign_Throws()
    {
        var tenantId = Guid.NewGuid();
        var (record, _, cs, _) = TransferredCall(tenantId);
        var def = CustomFieldDefinition.Create(tenantId, "upsell", "Upsell", CustomFieldDataType.String,
            clientId: Guid.NewGuid(), campaignId: Guid.NewGuid());
        _definitions.Setup(d => d.GetByIdAsync(def.Id, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService().SetValueFromScriptAsync(record.Id, cs.Id, def.Id, "x"));
        Assert.Null(cs.CustomFields);
    }
}
