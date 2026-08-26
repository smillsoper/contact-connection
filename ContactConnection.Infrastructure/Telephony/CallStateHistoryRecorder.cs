using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Telephony;

public class CallStateHistoryRecorder(
    ICallStateHistoryRepository repository,
    IDashboardNotifier dashboardNotifier,
    IConnectionMultiplexer redis) : ICallStateHistoryRecorder
{
    private readonly IDatabase _redis = redis.GetDatabase();

    public async Task RecordAsync(
        Guid tenantId,
        string tenantSchemaName,
        Guid callRecordId,
        string state,
        Guid campaignId,
        Guid? agentId,
        string? detail,
        string? abandonType = null,
        string? abandonLength = null,
        bool? metServiceLevel = null,
        CancellationToken ct = default)
    {
        var sequence = (int)await _redis.StringIncrementAsync($"callstate:seq:{callRecordId}");
        if (sequence == 1)
            await _redis.KeyExpireAsync($"callstate:seq:{callRecordId}", TimeSpan.FromHours(24));

        var entry = CallStateHistoryEntry.Create(
            tenantId, callRecordId, sequence, state, campaignId, agentId, detail, abandonType, abandonLength, metServiceLevel);

        await repository.AddAsync(entry, tenantSchemaName, ct);

        await dashboardNotifier.NotifyCallStateChangedAsync(tenantId, campaignId, state, ct);
    }
}
