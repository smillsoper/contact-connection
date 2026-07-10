using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.CallTrace;

/// <summary>
/// Single recording hook called by both flow engines after every node execution.
/// Persists unconditionally (durability), then fans out to any live subscriptions
/// watching this call.
/// </summary>
public class CallTraceRecorder(
    ICallTraceEventRepository repository,
    ICallTraceSubscriptionRegistry registry,
    ICallTraceNotifier notifier,
    IConnectionMultiplexer redis) : ICallTraceRecorder
{
    private readonly IDatabase _redis = redis.GetDatabase();

    public async Task RecordStepAsync(
        Guid tenantId,
        string tenantSchemaName,
        Guid callRecordId,
        string engine,
        string nodeId,
        string nodeType,
        string? label,
        string? detail,
        string? transitionTaken,
        string? nextNodeId,
        string? exitReason,
        Guid? campaignId,
        Guid? flowId,
        string? dnis,
        string? ani,
        CancellationToken ct = default)
    {
        var sequence = (int)await _redis.StringIncrementAsync($"calltrace:seq:{callRecordId}");
        if (sequence == 1)
            await _redis.KeyExpireAsync($"calltrace:seq:{callRecordId}", TimeSpan.FromHours(24));

        var trace = CallTraceEvent.Create(
            tenantId, callRecordId, sequence, engine, nodeId, nodeType, label, detail,
            transitionTaken, nextNodeId, exitReason, campaignId, flowId, dnis, ani);

        await repository.AddAsync(trace, tenantSchemaName, ct);

        var subscriptionIds = await registry.GetSubscriptionsForCallAsync(callRecordId, ct);
        if (subscriptionIds.Count == 0) return;

        var dto = new CallTraceStepDto
        {
            CallRecordId = callRecordId,
            Sequence = sequence,
            Engine = engine,
            NodeId = nodeId,
            NodeType = nodeType,
            Label = label,
            EnteredAt = trace.EnteredAt,
            Detail = detail,
            TransitionTaken = transitionTaken,
            ExitReason = exitReason,
            NextNodeId = nextNodeId,
        };

        foreach (var subscriptionId in subscriptionIds)
            await notifier.NotifyTraceStepAsync(subscriptionId, dto, ct);
    }
}
