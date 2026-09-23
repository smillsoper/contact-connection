using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.StoredValues;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.StoredValues;

/// <summary>
/// IStoredValueService is call-record-centric like ICustomFieldService — given a callRecordId, it
/// resolves which tenant/client/campaign "scope" actually applies by loading the call record, then
/// reads/writes the generic key-value table keyed by (tenantId, scope, scopeId, keyName).
/// </summary>
public class StoredValueServiceTests
{
    private static CallRecord NewCallRecord(Guid tenantId, Guid clientId, Guid campaignId) =>
        CallRecord.Create(tenantId, clientId, campaignId);

    private static (Mock<IStoredValueRepository> Values, Mock<ICallRecordRepository> CallRecords, StoredValueService Service) NewService(CallRecord record)
    {
        var values = new Mock<IStoredValueRepository>();
        var callRecords = new Mock<ICallRecordRepository>();
        callRecords.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var service = new StoredValueService(values.Object, callRecords.Object);
        return (values, callRecords, service);
    }

    [Fact]
    public async Task SetAsync_TenantScope_UsesGuidEmptyAsScopeId()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (values, _, service) = NewService(record);
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync((StoredValue?)null);

        await service.SetAsync(record.Id, StoredValueScope.Tenant, "key", "value", null, default);

        values.Verify(v => v.AddAsync(
            It.Is<StoredValue>(sv => sv.TenantId == tenantId && sv.Scope == StoredValueScope.Tenant && sv.ScopeId == Guid.Empty && sv.Value == "value"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_ClientScope_UsesTheCallsRealClientId()
    {
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, clientId, Guid.NewGuid());
        var (values, _, service) = NewService(record);
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Client, clientId, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync((StoredValue?)null);

        await service.SetAsync(record.Id, StoredValueScope.Client, "key", "value", null, default);

        values.Verify(v => v.AddAsync(
            It.Is<StoredValue>(sv => sv.ScopeId == clientId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_CampaignScope_UsesTheCallsRealCampaignId()
    {
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), campaignId);
        var (values, _, service) = NewService(record);
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Campaign, campaignId, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync((StoredValue?)null);

        await service.SetAsync(record.Id, StoredValueScope.Campaign, "key", "value", null, default);

        values.Verify(v => v.AddAsync(
            It.Is<StoredValue>(sv => sv.ScopeId == campaignId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_ExistingRow_UpdatesInPlace_DoesNotAddANewRow()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (values, _, service) = NewService(record);
        var existing = StoredValue.Create(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", "old", null);
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync(existing);

        await service.SetAsync(record.Id, StoredValueScope.Tenant, "key", "new", null, default);

        Assert.Equal("new", existing.Value);
        values.Verify(v => v.AddAsync(It.IsAny<StoredValue>(), It.IsAny<CancellationToken>()), Times.Never);
        values.Verify(v => v.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_UnknownScope_Throws()
    {
        var record = NewCallRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (_, _, service) = NewService(record);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetAsync(record.Id, "not_a_real_scope", "key", "value", null, default));
    }

    [Fact]
    public async Task SetAsync_CallRecordNotFound_Throws()
    {
        var values = new Mock<IStoredValueRepository>();
        var callRecords = new Mock<ICallRecordRepository>();
        callRecords.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallRecord?)null);
        var service = new StoredValueService(values.Object, callRecords.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetAsync(Guid.NewGuid(), StoredValueScope.Tenant, "key", "value", null, default));
    }

    [Fact]
    public async Task GetAsync_ExistingUnexpiredValue_ReturnsIt()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (values, _, service) = NewService(record);
        var stored = StoredValue.Create(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", "hello", null);
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync(stored);

        var result = await service.GetAsync(record.Id, StoredValueScope.Tenant, "key", default);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GetAsync_NoValueStored_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (values, _, service) = NewService(record);
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync((StoredValue?)null);

        var result = await service.GetAsync(record.Id, StoredValueScope.Tenant, "key", default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ExpiredValue_TreatedAsAbsent_NotYetSweptByTheRetentionJob()
    {
        var tenantId = Guid.NewGuid();
        var record = NewCallRecord(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var (values, _, service) = NewService(record);
        var expired = StoredValue.Create(
            tenantId, StoredValueScope.Tenant, Guid.Empty, "key", "stale", DateTimeOffset.UtcNow.AddMinutes(-1));
        values.Setup(v => v.GetAsync(tenantId, StoredValueScope.Tenant, Guid.Empty, "key", It.IsAny<CancellationToken>()))
              .ReturnsAsync(expired);

        var result = await service.GetAsync(record.Id, StoredValueScope.Tenant, "key", default);

        Assert.Null(result);
    }
}
