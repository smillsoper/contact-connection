using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.StoredValues;

public class StoredValueService : IStoredValueService
{
    private readonly IStoredValueRepository _values;
    private readonly ICallRecordRepository _callRecords;

    public StoredValueService(IStoredValueRepository values, ICallRecordRepository callRecords)
    {
        _values = values;
        _callRecords = callRecords;
    }

    public async Task<string?> GetAsync(Guid callRecordId, string scope, string keyName, CancellationToken ct = default)
    {
        var (tenantId, scopeId) = await ResolveScopeAsync(callRecordId, scope, ct);
        var value = await _values.GetAsync(tenantId, scope, scopeId, keyName, ct);

        // An expired-but-not-yet-swept row is treated as absent — the retention job is a periodic
        // sweep, not instant, so a read can't rely on rows being physically gone the moment they expire.
        if (value is null || value.IsExpired(DateTimeOffset.UtcNow)) return null;
        return value.Value;
    }

    public async Task SetAsync(
        Guid callRecordId, string scope, string keyName, string value, DateTimeOffset? expiresAt, CancellationToken ct = default)
    {
        var (tenantId, scopeId) = await ResolveScopeAsync(callRecordId, scope, ct);
        var existing = await _values.GetAsync(tenantId, scope, scopeId, keyName, ct);

        if (existing is not null)
        {
            existing.UpdateValue(value, expiresAt);
        }
        else
        {
            await _values.AddAsync(StoredValue.Create(tenantId, scope, scopeId, keyName, value, expiresAt), ct);
        }

        await _values.SaveChangesAsync(ct);
    }

    private async Task<(Guid TenantId, Guid ScopeId)> ResolveScopeAsync(Guid callRecordId, string scope, CancellationToken ct)
    {
        if (!StoredValueScope.All.Contains(scope))
            throw new ArgumentException($"Unknown scope: {scope}", nameof(scope));

        var record = await _callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found");

        var scopeId = scope switch
        {
            StoredValueScope.Client => record.ClientId,
            StoredValueScope.Campaign => record.CampaignId,
            _ => Guid.Empty, // StoredValueScope.Tenant
        };

        return (record.TenantId, scopeId);
    }
}
